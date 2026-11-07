#!/usr/bin/env python3
"""
Grid-bot backtester supporting dynamic bar-by-bar ATR re-gridding,
adjacent-level grid arbitrage profit tracking, and profit margins relative to initial capital.

Input:
    A semicolon-separated CSV with columns:
    Date;Open;High;Low;Close;Volume

ATR window (--atr-window-multiplier):
    ATR mode only. By default the ATR grid always spans the full static
    --grid-min/--grid-max range; only the level *spacing* adapts to ATR. Setting
    --atr-window-multiplier additionally shrinks the *active span* itself to a
    volatility-sized band around the current price -- [price - ATR*mult, price
    + ATR*mult], clipped to grid_min/grid_max -- so the grid is tighter in calm
    markets and wider (fewer levels blown through in one direction) in volatile
    ones. Independent of --atr-multiplier, which still controls spacing within
    whatever span is in effect.

Sticky ATR window (--atr-window-sticky):
    ATR mode only. With a window multiplier, keep the active window centered at
    its previous center and recenter it only after price leaves the window.
    This reduces churn during directional moves while retaining the normal
    volatility-based window sizing. Without this flag, the window recenters
    every bar as before.

Strategy variants (--strategy):
    standard    Default behaviour: buy every level on the way down, sell every
                level on the way up.
    every4      Buy only on every 4th grid level (4x the base grid spacing).
                Each buy is unwound in four equal (25%) tranches, sold at each
                of the 4 base-grid levels above the buy level.
    half-split  Buy only at grid levels in the lower half of the grid, sell
                only at grid levels in the upper half of the grid.

Starting position:
    The bot always starts with 100% cash / 0% asset. By default, before the
    first bar is processed, it performs an "initial fill": buy limit orders
    below the starting price are left unfilled (they execute normally, later,
    when price actually dips through them during the backtest). What gets
    pre-funded instead is the sell side: to rest a sell limit order at a
    level above the starting price, the bot must already own the asset for
    it, so the initial fill buys -- in one lump transaction at the starting
    price -- exactly enough asset to cover every sell-eligible level above
    the starting price, sized the same as any other sell (`trade_notional`
    worth of value at that level). The higher the starting price sits in the
    grid, the fewer levels remain above it and the smaller this buy is; at
    grid_max there are no levels left above, so nothing is bought at all. At
    grid_min, nearly the whole grid sits above, so this is at its largest.
    'every4' sell targets above the starting price are also pre-funded. They
    are represented as synthetic lots bought at the starting price, so they
    can be sold normally if those targets are reached.

Independent flag:
    --no-initial-assets   Skip the initial fill. The bot starts with 100%
                           cash and 0% asset and acquires inventory only as
                           the price actually dips through buy levels during
                           the backtest. Any sell level touched before then
                           silently executes as an "empty sell" (no trade
                           recorded, no cash/asset change) -- the level below
                           it simply remains a live buy point, which is
                           already how the direction-based crossing logic
                           behaves. This flag can be combined with any
                           --strategy.

Examples:
    Static 2% grid, standard strategy:
        python3 grid_backtest.py --csv data.csv --year 2023 \\
            --grid-mode static --grid-size 0.02 --grid-min 80 --grid-max 120

    ATR-adaptive grid (full static span, only spacing adapts):
        python3 grid_backtest.py --csv data.csv --year 2023 \\
            --grid-mode atr --atr-multiplier 1.0 --grid-min 80 --grid-max 120

    ATR-adaptive grid with a dynamic, volatility-sized window,
    'every4' strategy, no pre-funded initial fill, CSVs exported:
        python3 grid_backtest.py --csv data.csv --year 2023 \\
            --grid-mode atr --atr-multiplier 1.0 --atr-window-multiplier 5 \\
            --grid-min 80 --grid-max 120 --strategy every4 \\
            --no-initial-assets --write-csv --output-prefix run1
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, asdict
from pathlib import Path

import pandas as pd


INITIAL_CAPITAL = 60_000.00
TRANSACTION_RATE = 0.0000
MIN_TRANSACTION_COST = 0.00
EVERY4_TRANCHES = 4


@dataclass
class Trade:
    date: str
    side: str
    price: float
    quantity: float
    gross_value: float
    transaction_cost: float
    cash_after: float
    asset_after: float


def compute_atr(df: pd.DataFrame, period: int = 14) -> pd.Series:
    """Calculate the Average True Range (ATR)."""
    high = df["High"]
    low = df["Low"]
    close_prev = df["Close"].shift(1)

    tr1 = high - low
    tr2 = (high - close_prev).abs()
    tr3 = (low - close_prev).abs()

    tr = pd.concat([tr1, tr2, tr3], axis=1).max(axis=1)
    return tr.rolling(window=period, min_periods=1).mean()


def geometric_grid(grid_min: float, grid_max: float, grid_size: float) -> list[float]:
    """Generate fixed geometric percentage-based grid levels."""
    if grid_min <= 0 or grid_max <= grid_min:
        raise ValueError("grid_min must be > 0 and grid_max must be greater than grid_min.")
    if grid_size <= 0:
        raise ValueError("grid_size must be greater than 0.")

    levels = []
    price = grid_min
    while price <= grid_max * (1 + 1e-12):
        levels.append(price)
        price *= 1 + grid_size

    return levels


def generate_atr_grid(
    center_price: float,
    grid_min: float,
    grid_max: float,
    current_atr: float,
    atr_multiplier: float,
    window_multiplier: float | None = None,
) -> list[float]:
    """
    Dynamically generate grid levels centered around current price within bounds.

    `grid_min`/`grid_max` are the static outer bounds the price is validated against
    and the window can never extend past. `window_multiplier`, if given, additionally
    shrinks the *active* span down to a volatility-sized band around `center_price`:
    [center - current_atr * window_multiplier, center + current_atr * window_multiplier],
    clipped to [grid_min, grid_max]. This keeps the grid tight (fewer, closer-spaced
    levels) in calm markets and wide (fewer levels blown through in one direction) in
    volatile ones, on top of the existing ATR-based level *spacing*. If omitted (None
    or <= 0), the window is just the full static [grid_min, grid_max] range, matching
    the original behaviour.
    """
    step = current_atr * atr_multiplier
    if step <= 0:
        step = current_atr
    if step <= 0:
        # ATR is zero (e.g. a perfectly flat bar or lookback window with no
        # High-Low range at all) or negative (shouldn't happen, but a hostile
        # atr_multiplier could get us here). Both fallbacks above would leave
        # step <= 0, and `p -= step` / `p += step` would then never change `p`,
        # looping forever. Fall back to a small step derived from the price
        # itself so the grid still builds instead of hanging.
        step = max(center_price * 0.001, 1e-8)

    if window_multiplier is not None and window_multiplier > 0:
        window_radius = current_atr * window_multiplier
        if window_radius <= 0:
            window_radius = step
        window_min = max(grid_min, center_price - window_radius)
        window_max = min(grid_max, center_price + window_radius)
        # Guard against a degenerate/inverted window -- e.g. window_radius smaller
        # than a single step, or center_price sitting right at grid_min/grid_max --
        # by ensuring at least one step of room on each side.
        if window_max - window_min < step:
            window_min = max(grid_min, center_price - step)
            window_max = min(grid_max, center_price + step)
    else:
        window_min, window_max = grid_min, grid_max

    levels = []
    # Build downwards from center
    p = center_price
    while p >= window_min:
        levels.append(p)
        p -= step

    # Build upwards from center
    p = center_price + step
    while p <= window_max:
        levels.append(p)
        p += step

    levels = sorted(list(set(levels)))
    if not levels or levels[0] > window_min:
        levels.insert(0, window_min)
    if levels[-1] < window_max:
        levels.append(window_max)

    return levels


def calculate_grid_size_pct(levels: list[float]) -> float:
    """Calculate average grid step size as percentage of level prices."""
    if len(levels) < 2:
        return 0.0
    steps = [(levels[i+1] - levels[i]) / levels[i] * 100 for i in range(len(levels) - 1)]
    return sum(steps) / len(steps)


def build_every4_structures(levels: list[float]) -> tuple[list[float], dict[float, list[float]]]:
    """
    For the 'every4' strategy: buy points are every 4th grid level (index 0, 4, 8, ...).
    Each buy point maps to the up-to-4 base-grid levels immediately above it, which
    will each absorb a 25% tranche of that buy's quantity on the way up.
    """
    buy_points: list[float] = []
    sell_map: dict[float, list[float]] = {}
    for i in range(0, len(levels), EVERY4_TRANCHES):
        if i >= len(levels) - 1:
            break  # no levels above this one left to act as sell tranches
        targets = levels[i + 1: i + 1 + EVERY4_TRANCHES]
        buy_points.append(levels[i])
        sell_map[levels[i]] = targets
    return buy_points, sell_map


def build_half_split_structures(levels: list[float]) -> tuple[set[float], set[float]]:
    """
    For the 'half-split' strategy: buys are only allowed at grid levels in the lower
    half of the grid, sells only at grid levels in the upper half. The midpoint level
    is included in both halves so it can act as either.
    """
    if len(levels) < 2:
        full = set(levels)
        return full, full
    mid_price = levels[len(levels) // 2]
    buy_allowed = {lvl for lvl in levels if lvl <= mid_price}
    sell_allowed = {lvl for lvl in levels if lvl >= mid_price}
    return buy_allowed, sell_allowed


def transaction_cost(value: float) -> float:
    return max(value * TRANSACTION_RATE, MIN_TRANSACTION_COST)


def load_data(filename: str, year: int, atr_period: int = 14) -> pd.DataFrame:
    df = pd.read_csv(
        filename,
        sep=";",
        quotechar='"',
        thousands=",",
        decimal=".",
    )

    required = {"Date", "Open", "High", "Low", "Close"}
    missing = required - set(df.columns)
    if missing:
        raise ValueError(f"CSV is missing columns: {sorted(missing)}")

    df["Date"] = pd.to_datetime(df["Date"], format="%A, %B %d, %Y")
    for column in ["Open", "High", "Low", "Close"]:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    df = df.dropna(subset=["Date", "Open", "High", "Low", "Close"]).sort_values("Date").reset_index(drop=True)
    if atr_period <= 0:
        raise ValueError("atr_period must be greater than 0.")
    df["ATR"] = compute_atr(df, atr_period)

    # ATR[i] is computed from High[i]/Low[i] -- i.e. today's own intraday range --
    # which isn't actually known until today's bar has finished trading. Using it to
    # decide today's grid *before* walking the bar's Open/Low/High/Close path is
    # look-ahead bias. ATR_Prior holds the previous bar's ATR (shifted forward one
    # row), which is what would genuinely be known at the start of the current bar,
    # and is what dynamic ATR re-gridding should use instead. This is computed here,
    # before the year filter, so the first bars of `year` can still see ATR from the
    # tail end of the prior year rather than losing their warm-up.
    df["ATR_Prior"] = df["ATR"].shift(1)

    # Filter for target backtest year
    df = df[df["Date"].dt.year == year].reset_index(drop=True)
    if df.empty:
        raise ValueError(f"No data found for year {year}.")

    return df


def crossed_levels(a: float, b: float, levels: list[float]) -> list[float]:
    """Return levels crossed from a to b, in the direction of travel."""
    if b > a:
        return [x for x in levels if a < x <= b]
    if b < a:
        return [x for x in reversed(levels) if b <= x < a]
    return []


def backtest(
    df: pd.DataFrame,
    grid_min: float,
    grid_max: float,
    grid_mode: str,
    grid_size: float | None,
    atr_period: int | None,
    atr_multiplier: float | None,
    atr_window_multiplier: float | None,
    path: str,
    strategy: str = "standard",
    no_initial_assets: bool = False,
    atr_window_sticky: bool = False,
) -> tuple[pd.DataFrame, pd.DataFrame, dict]:

    if strategy not in ("standard", "every4", "half-split"):
        raise ValueError(f"Unsupported strategy: {strategy}")

    start_price = float(df.iloc[0]["Open"])
    if not grid_min <= start_price <= grid_max:
        raise ValueError(
            f"First price ({start_price:.2f}) is outside the grid "
            f"({grid_min:.2f} - {grid_max:.2f})."
        )

    # Initial Grid Generation
    if grid_mode == "static":
        if grid_size is None:
            raise ValueError("--grid-size is required when --grid-mode is static.")
        levels = geometric_grid(grid_min, grid_max, grid_size)
    elif grid_mode == "atr":
        if atr_multiplier is None:
            raise ValueError("--atr-multiplier is required when --grid-mode is atr.")
        # Use the prior bar's ATR (known before this bar opens) rather than this
        # bar's own ATR (which bakes in today's own High/Low -- look-ahead bias).
        # Only falls back to the same-bar ATR if there's truly no earlier bar at
        # all in the source CSV (e.g. backtesting the very first year of data).
        initial_atr_prior = df.iloc[0]["ATR_Prior"]
        initial_atr = (
            float(initial_atr_prior) if not pd.isna(initial_atr_prior) else float(df.iloc[0]["ATR"])
        )
        levels = generate_atr_grid(
            start_price, grid_min, grid_max, initial_atr, atr_multiplier, atr_window_multiplier
        )
    else:
        raise ValueError(f"Unsupported grid mode: {grid_mode}")

    cash = INITIAL_CAPITAL
    asset = 0.0

    # Strategy-specific derived structures (recomputed whenever `levels` changes).
    every4_buy_points: list[float] = []
    every4_sell_map: dict[float, list[float]] = {}
    buy_allowed: set[float] = set()
    sell_allowed: set[float] = set()

    def refresh_strategy_state() -> float:
        """Recompute strategy-dependent structures + trade sizing for the current `levels`."""
        nonlocal every4_buy_points, every4_sell_map, buy_allowed, sell_allowed
        if strategy == "every4":
            every4_buy_points, every4_sell_map = build_every4_structures(levels)
            return INITIAL_CAPITAL / max(1, len(every4_buy_points))
        elif strategy == "half-split":
            buy_allowed, sell_allowed = build_half_split_structures(levels)
            return INITIAL_CAPITAL / max(1, len(levels) - 1)
        else:
            return INITIAL_CAPITAL / max(1, len(levels) - 1)

    trade_notional = refresh_strategy_state()
    window_center = start_price
    active_window_min = levels[0]
    active_window_max = levels[-1]

    trades: list[Trade] = []
    equity_curve = []
    current_price = start_price

    realized_arb_profit = 0.0
    total_sell_volume = 0.0
    grid_sizes_pct = []
    window_lows: list[float] = []
    window_highs: list[float] = []
    empty_sell_events = 0

    # Running weighted-average cost basis of currently held inventory (sum of gross
    # buy value for units not yet sold). Used to compute realized arb profit on sells
    # that don't carry an explicit buy_price_ref (i.e. everything outside 'every4').
    # See execute_sell for why this replaced a grid-level-based proxy.
    total_asset_cost = 0.0

    # Open "lots" for the every4 strategy: each is a buy fill waiting to be unwound
    # in tranches at its associated sell levels.
    lots: list[dict] = []

    def execute_buy(price: float, date: str, requested_notional: float | None = None) -> float:
        nonlocal cash, asset, total_asset_cost

        target_notional = trade_notional if requested_notional is None else requested_notional
        gross = min(target_notional, cash / (1 + TRANSACTION_RATE))
        if gross <= 0:
            return 0.0

        cost = transaction_cost(gross)
        total_cash_needed = gross + cost

        if total_cash_needed > cash:
            gross = max(0.0, cash - MIN_TRANSACTION_COST)
            cost = transaction_cost(gross)
            if gross <= 0 or gross + cost > cash:
                return 0.0

        quantity = gross / price
        cash -= gross + cost
        asset += quantity
        total_asset_cost += gross  # cost basis is tracked pre-fee, matching how
        # "Gross Realized Arb Profit" is reported (fees are subtracted separately
        # to get the net figure).

        trades.append(
            Trade(
                date=date,
                side="BUY",
                price=price,
                quantity=quantity,
                gross_value=gross,
                transaction_cost=cost,
                cash_after=cash,
                asset_after=asset,
            )
        )
        return quantity

    def execute_sell(
        price: float,
        date: str,
        requested_quantity: float | None = None,
        buy_price_ref: float | None = None,
    ) -> float:
        nonlocal cash, asset, realized_arb_profit, total_sell_volume, empty_sell_events, total_asset_cost

        if requested_quantity is not None:
            quantity = min(requested_quantity, asset)
            gross = quantity * price
        else:
            gross = min(trade_notional, asset * price)
            quantity = gross / price if price > 0 else 0.0
            if quantity > asset:
                quantity = asset
                gross = quantity * price

        if gross <= 0 or quantity <= 0:
            # "Empty sell": a sell level was touched but there's nothing to sell yet.
            # This is now rare since the initial fill seeds inventory for every buy
            # level below the starting price, but it can still happen -- e.g. ATR
            # re-gridding introduces a new sell level above any held inventory, or a
            # strategy variant restricts which levels may hold open lots. No trade is
            # recorded; the level below simply remains a live buy point, since buy/sell
            # is already determined purely by the direction of the price crossing.
            empty_sell_events += 1
            return 0.0

        # Weighted-average cost basis of the inventory being drawn down, computed
        # BEFORE `asset` is reduced below. This used to be approximated as "whichever
        # grid level currently sits just below the sale price" -- a fine proxy on a
        # static grid (the level below really was the level that filled the buy), but
        # wrong under ATR dynamic-window re-gridding: the grid gets rebuilt around the
        # current price every bar, so "the level below" at sale time has no relation
        # to what was actually paid for that inventory. Tracking real cost keeps this
        # correct regardless of how much the grid itself moves.
        avg_cost_basis = total_asset_cost / asset if asset > 0 else price
        # The exact cost removed from the shared pool: use the precise per-lot price
        # when the caller knows it (every4), otherwise the weighted average above.
        cost_basis_removed = buy_price_ref if buy_price_ref is not None else avg_cost_basis

        cost = transaction_cost(gross)
        asset -= quantity
        cash += gross - cost
        total_sell_volume += gross
        total_asset_cost -= cost_basis_removed * quantity

        if buy_price_ref is not None:
            # 'every4' path: each tranche already knows exactly which buy(s) funded
            # it (see process_leg_every4), so this is exact, not a proxy.
            realized_arb_profit += quantity * (price - buy_price_ref)
        else:
            realized_arb_profit += quantity * (price - avg_cost_basis)

        trades.append(
            Trade(
                date=date,
                side="SELL",
                price=price,
                quantity=quantity,
                gross_value=gross,
                transaction_cost=cost,
                cash_after=cash,
                asset_after=asset,
            )
        )
        return quantity

    def process_leg_standard(a: float, b: float, date: str) -> None:
        for level in crossed_levels(a, b, levels):
            if level < a:
                execute_buy(level, date)
            else:
                execute_sell(level, date)

    def process_leg_half_split(a: float, b: float, date: str) -> None:
        for level in crossed_levels(a, b, levels):
            if level < a:
                if level in buy_allowed:
                    execute_buy(level, date)
            else:
                if level in sell_allowed:
                    execute_sell(level, date)

    def process_leg_every4(a: float, b: float, date: str) -> None:
        # Buys: only at the every-4th buy points, only on the way down.
        for level in crossed_levels(a, b, every4_buy_points):
            if level < a:
                qty = execute_buy(level, date, requested_notional=trade_notional)
                if qty > 0:
                    targets = every4_sell_map.get(level, [])
                    if targets:
                        lots.append(
                            {
                                "buy_level": level,
                                "qty_per_target": qty / len(targets),
                                "targets": list(targets),
                            }
                        )

        # Sells: check crossings against whatever sell targets are still pending across
        # all open lots. These are literal price levels tied to each lot, independent
        # of the current `levels` array, so this stays correct even as ATR re-gridding
        # changes the grid out from under open positions.
        if lots:
            pending_targets = sorted({t for lot in lots for t in lot["targets"]})
            for level in crossed_levels(a, b, pending_targets):
                if level > a:
                    total_qty = 0.0
                    weighted_buy_sum = 0.0
                    for lot in lots:
                        if level in lot["targets"]:
                            q = lot["qty_per_target"]
                            total_qty += q
                            weighted_buy_sum += q * lot["buy_level"]
                            lot["targets"].remove(level)
                    lots[:] = [lot for lot in lots if lot["targets"]]
                    if total_qty > 0:
                        buy_ref = weighted_buy_sum / total_qty
                        execute_sell(level, date, requested_quantity=total_qty, buy_price_ref=buy_ref)

    # Initial fill.
    #
    # Buy limit orders below the starting price are NOT executed here -- they are
    # unfilled resting orders that only fire later, in the normal bar-by-bar loop,
    # when price actually dips through them. That already happens for free with
    # cash=INITIAL_CAPITAL / asset=0.0 above, no special-casing needed.
    #
    # What DOES need pre-funding at t=0 is the sell side: to rest a sell limit order
    # at a level above the starting price, the bot must already own the asset for it
    # -- it can't sell what it doesn't have. So the initial fill buys, in one lump
    # transaction at the starting price, exactly enough asset to cover every
    # sell-eligible level above the starting price (sized the same as any other sell:
    # `trade_notional` worth of value at that level, i.e. `trade_notional / level`
    # units). The higher the starting price sits in the grid, the fewer levels remain
    # above it and the less this buys; at grid_max there are no levels left above, so
    # nothing is bought at all. At grid_min, nearly the whole grid sits above, so this
    # is at its largest.
    #
    # For 'every4', sell targets above the starting price are pre-funded as
    # synthetic one-target lots. Limit the funding to one normal buy allocation
    # for every buy point above the start plus the nearest buy point at or below
    # it. This keeps all relevant sell targets live without funding the entire
    # four-tranche capacity of every buy point in the grid.
    #
    # --no-initial-assets skips this entirely and leaves the bot in pure 100% cash /
    # 0% asset, with sell levels touched before any buy executing as "empty sells"
    # (see execute_sell) until the price actually dips and fills them.
    initial_fill_date = df.iloc[0]["Date"].strftime("%Y-%m-%d")
    trades_before_fill = len(trades)
    cash_before_fill = cash
    if not no_initial_assets:
        if strategy == "standard":
            sell_levels_above = [lvl for lvl in levels if lvl > start_price]
        elif strategy == "half-split":
            sell_levels_above = [lvl for lvl in levels if lvl > start_price and lvl in sell_allowed]
        else:  # every4: pre-fund each distinct sell target above the start.
            sell_levels_above = sorted(
                {
                    target
                    for targets in every4_sell_map.values()
                    for target in targets
                    if target > start_price
                }
            )

        initial_funding_limit = None
        if strategy == "every4":
            buy_points_above = sum(level > start_price for level in every4_buy_points)
            has_buy_point_at_or_below = any(level <= start_price for level in every4_buy_points)
            funding_slots = buy_points_above + int(has_buy_point_at_or_below)
            if funding_slots > 0:
                # Include the transaction fee in the cap so the deployed cash
                # cannot exceed the number of funded buy allocations.
                initial_funding_limit = (
                    trade_notional * funding_slots / (1 + TRANSACTION_RATE)
                )

        if sell_levels_above:
            quantity_needed = sum(trade_notional / lvl for lvl in sell_levels_above)
            notional_needed = quantity_needed * start_price
            if initial_funding_limit is not None:
                notional_needed = min(notional_needed, initial_funding_limit)
            initial_quantity = execute_buy(
                start_price,
                initial_fill_date,
                requested_notional=notional_needed,
            )
            if strategy == "every4" and initial_quantity > 0:
                allocation_ratio = initial_quantity / quantity_needed
                for target in sell_levels_above:
                    lots.append(
                        {
                            "buy_level": start_price,
                            "qty_per_target": (trade_notional / target) * allocation_ratio,
                            "targets": [target],
                        }
                    )
    initial_fill_trades = len(trades) - trades_before_fill
    initial_fill_cost = cash_before_fill - cash
    initial_asset_after_fill = asset

    for _, row in df.iterrows():
        date = row["Date"].strftime("%Y-%m-%d")
        open_price = float(row["Open"])
        high = float(row["High"])
        low = float(row["Low"])
        close = float(row["Close"])
        # Prior-bar ATR: what would actually be known at the start of this bar,
        # before its own High/Low are observed. See ATR_Prior comment in load_data.
        daily_atr = float(row["ATR_Prior"]) if not pd.isna(row["ATR_Prior"]) else float(row["ATR"])

        # Dynamic Bar-by-Bar ATR Re-gridding. A sticky window keeps its center
        # until price exits the current active span, avoiding bar-by-bar
        # price-chasing while preserving the default behavior when disabled.
        if grid_mode == "atr" and not pd.isna(daily_atr):
            if (
                not atr_window_sticky
                or atr_window_multiplier is None
                or atr_window_multiplier <= 0
                or current_price < active_window_min
                or current_price > active_window_max
            ):
                window_center = current_price
            levels = generate_atr_grid(
                window_center,
                grid_min,
                grid_max,
                daily_atr,
                atr_multiplier,
                atr_window_multiplier,
            )
            active_window_min = levels[0]
            active_window_max = levels[-1]
            trade_notional = refresh_strategy_state()

        # Track grid spacing % and active window bounds during execution
        grid_sizes_pct.append(calculate_grid_size_pct(levels))
        if levels:
            window_lows.append(levels[0])
            window_highs.append(levels[-1])

        if path == "OLHC":
            points = [open_price, low, high, close]
        elif path == "OHLC":
            points = [open_price, high, low, close]
        else:
            raise ValueError("path must be OLHC or OHLC")

        for next_price in points:
            if strategy == "every4":
                process_leg_every4(current_price, next_price, date)
            elif strategy == "half-split":
                process_leg_half_split(current_price, next_price, date)
            else:
                process_leg_standard(current_price, next_price, date)

            current_price = next_price

        equity = cash + asset * close
        equity_curve.append(
            {
                "Date": row["Date"],
                "Close": close,
                "Cash": cash,
                "Asset": asset,
                "PortfolioValue": equity,
            }
        )

    trades_df = pd.DataFrame(asdict(t) for t in trades)
    curve_df = pd.DataFrame(equity_curve)

    final_price = float(df.iloc[-1]["Close"])
    final_value = cash + asset * final_price
    total_costs = sum(t.transaction_cost for t in trades)
    net_arb_profit = realized_arb_profit - total_costs

    # Profit Margins (%) calculated against Initial Capital
    gross_arb_margin_pct = (realized_arb_profit / INITIAL_CAPITAL) * 100
    net_arb_margin_pct = (net_arb_profit / INITIAL_CAPITAL) * 100

    # Buy & hold benchmark: always assumes the full initial capital was used to buy the
    # asset at the start price, since that's the standard "just hold it" comparison --
    # independent of however much (if any) the grid bot's initial fill acquired.
    buy_hold_asset = INITIAL_CAPITAL / start_price
    buy_hold_final = buy_hold_asset * final_price

    curve_df["Peak"] = curve_df["PortfolioValue"].cummax()
    curve_df["Drawdown"] = curve_df["PortfolioValue"] / curve_df["Peak"] - 1
    max_drawdown = curve_df["Drawdown"].min()

    summary = {
        "strategy": strategy,
        "no_initial_assets": no_initial_assets,
        "initial_fill_trades": initial_fill_trades,
        "initial_fill_cost": initial_fill_cost,
        "initial_asset_after_fill": initial_asset_after_fill,
        "atr_multiplier": atr_multiplier,
        "atr_window_multiplier": atr_window_multiplier,
        "atr_window_sticky": atr_window_sticky,
        "window_min_seen": min(window_lows) if window_lows else None,
        "window_max_seen": max(window_highs) if window_highs else None,
        "window_min_final": window_lows[-1] if window_lows else None,
        "window_max_final": window_highs[-1] if window_highs else None,
        "grid_mode": grid_mode.upper(),
        "grid_min": grid_min,
        "grid_max": grid_max,
        "grid_levels": len(levels),
        "grid_intervals": len(levels) - 1,
        "grid_size_min_pct": min(grid_sizes_pct) if grid_sizes_pct else 0.0,
        "grid_size_max_pct": max(grid_sizes_pct) if grid_sizes_pct else 0.0,
        "grid_size_final_pct": grid_sizes_pct[-1] if grid_sizes_pct else 0.0,
        "start_price": start_price,
        "end_price": final_price,
        "final_cash": cash,
        "final_asset": asset,
        "final_value": final_value,
        "total_profit_loss": final_value - INITIAL_CAPITAL,
        "total_return_pct": (final_value / INITIAL_CAPITAL - 1) * 100,
        "gross_realized_arb_profit": realized_arb_profit,
        "net_realized_arb_profit": net_arb_profit,
        "total_sell_volume": total_sell_volume,
        "gross_arb_margin_pct": gross_arb_margin_pct,
        "net_arb_margin_pct": net_arb_margin_pct,
        "buy_hold_final": buy_hold_final,
        "buy_hold_return_pct": (buy_hold_final / INITIAL_CAPITAL - 1) * 100,
        "grid_vs_buy_hold": final_value - buy_hold_final,
        "grid_vs_buy_hold_pct_points": (final_value / INITIAL_CAPITAL - 1) * 100
        - (buy_hold_final / INITIAL_CAPITAL - 1) * 100,
        "transaction_costs": total_costs,
        "number_of_trades": len(trades),
        "buys": sum(t.side == "BUY" for t in trades),
        "sells": sum(t.side == "SELL" for t in trades),
        "max_drawdown_pct": max_drawdown * 100,
        "empty_sell_events": empty_sell_events,
    }

    if strategy == "every4":
        summary["buy_grid_size_pct"] = calculate_grid_size_pct(every4_buy_points)
        summary["buy_points_count"] = len(every4_buy_points)
        summary["open_lots_at_end"] = len(lots)
    elif strategy == "half-split":
        summary["half_split_mid_price"] = levels[len(levels) // 2] if levels else None
        summary["buy_levels_count"] = len(buy_allowed)
        summary["sell_levels_count"] = len(sell_allowed)

    return trades_df, curve_df, summary


def print_summary(summary: dict, year: int) -> None:
    # ANSI styling for the headline P&L figures, so they stand out from the rest of
    # the report. Only applied when stdout is an actual terminal -- piping/redirecting
    # output (e.g. `> log.txt`, `| tee`) gets plain text instead of escape codes.
    use_color = sys.stdout.isatty()
    BOLD = "\033[1m" if use_color else ""
    GREEN = "\033[32m" if use_color else ""
    RED = "\033[31m" if use_color else ""
    RESET = "\033[0m" if use_color else ""

    def highlight(text: str, positive: bool) -> str:
        color = GREEN if positive else RED
        return f"{BOLD}{color}{text}{RESET}"

    def signed(value: float, decimals: int = 2) -> str:
        return f"+{value:,.{decimals}f}" if value >= 0 else f"{value:,.{decimals}f}"

    print(f"\n=== GRID BACKTEST {year} ({summary['grid_mode']} MODE, {summary['strategy'].upper()} STRATEGY) ===")
    if summary["no_initial_assets"]:
        print("Starting position:       100% cash (no initial fill)")
    else:
        print("Starting position:       100% cash")
        print(
            f"Initial fill:            {summary['initial_fill_trades']} buy(s), "
            f"\u20ac{summary['initial_fill_cost']:,.2f} deployed, "
            f"{summary['initial_asset_after_fill']:.6f} units acquired"
        )
    print(f"Grid Bounds:             \u20ac{summary['grid_min']:.2f} - \u20ac{summary['grid_max']:.2f}")
    print(f"ATR multiplier:          {summary['atr_multiplier']}")
    if summary["atr_window_multiplier"]:
        print(f"ATR window multiplier:   {summary['atr_window_multiplier']}")
        if summary["atr_window_sticky"]:
            print("ATR window recentering:  sticky (recenter on window exit)")
        print(
            f"Dynamic window range:    \u20ac{summary['window_min_seen']:.2f} - \u20ac{summary['window_max_seen']:.2f} seen "
            f"(final: \u20ac{summary['window_min_final']:.2f} - \u20ac{summary['window_max_final']:.2f})"
        )
    print(f"Final Grid Levels:       {summary['grid_levels']}")
    print(f"Grid Size Range (%):     {summary['grid_size_min_pct']:.2f}% to {summary['grid_size_max_pct']:.2f}% (Final: {summary['grid_size_final_pct']:.2f}%)")
    if summary["strategy"] == "every4":
        print(f"Buy Points:              {summary['buy_points_count']} (avg spacing {summary['buy_grid_size_pct']:.2f}%)")
        print(f"Open lots at end:        {summary['open_lots_at_end']}")
    if summary["strategy"] == "half-split":
        print(f"Half-split midpoint:     \u20ac{summary['half_split_mid_price']:.2f}")
        print(f"Buy levels / Sell levels: {summary['buy_levels_count']} / {summary['sell_levels_count']}")
    print(f"Starting price:          \u20ac{summary['start_price']:.2f}")
    print(f"Ending price:            \u20ac{summary['end_price']:.2f}")
    print()
    print(f"Initial capital:         \u20ac{INITIAL_CAPITAL:,.2f}")
    print(f"Final portfolio value:    \u20ac{summary['final_value']:,.2f}")

    total_pnl_positive = summary["total_profit_loss"] >= 0
    total_pnl_line = (
        f"TOTAL PROFIT/LOSS: \u20ac{signed(summary['total_profit_loss'])} "
        f"({signed(summary['total_return_pct'])}%)"
    )
    border = "=" * (len(total_pnl_line) + 4)
    print(border)
    print(f"  {highlight(total_pnl_line, total_pnl_positive)}")
    print(border)
    print()
    print("--- ARBITRAGE PERFORMANCE & MARGINS ---")
    print(f"Gross Realized Arb PnL:  \u20ac{summary['gross_realized_arb_profit']:,.2f}")
    print(f"Total Transaction Fees:  \u20ac{summary['transaction_costs']:,.2f}")
    print(f"Net Realized Arb PnL:    \u20ac{summary['net_realized_arb_profit']:,.2f}")
    print(f"Gross Arb Profit Margin: {summary['gross_arb_margin_pct']:.2f}% (Gross Arb / Initial Capital)")
    print(f"Net Arb Profit Margin:   {summary['net_arb_margin_pct']:.2f}% (Net Arb / Initial Capital)")
    print()
    print(f"Buy & hold value:         \u20ac{summary['buy_hold_final']:,.2f}")
    print(f"Buy & hold return:        {summary['buy_hold_return_pct']:.2f}%")

    vs_bh_positive = summary["grid_vs_buy_hold"] >= 0
    vs_bh_line = (
        f"VS BUY & HOLD: \u20ac{signed(summary['grid_vs_buy_hold'])} "
        f"({signed(summary['grid_vs_buy_hold_pct_points'])} pp)"
    )
    border = "=" * (len(vs_bh_line) + 4)
    print(border)
    print(f"  {highlight(vs_bh_line, vs_bh_positive)}")
    print(border)
    print()
    print(f"Trades (Buys/Sells):      {summary['number_of_trades']} ({summary['buys']} / {summary['sells']})")
    print(f"Empty sell events:        {summary['empty_sell_events']}")
    print(f"Maximum drawdown:         {summary['max_drawdown_pct']:.2f}%")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Dynamic ATR and Grid Step Arbitrage Backtester.",
        epilog=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--csv", required=True, help="Path to the CSV file.")
    parser.add_argument("--year", type=int, required=True, help="Year to backtest.")
    parser.add_argument(
        "--grid-mode",
        choices=["static", "atr"],
        default="static",
        help="Grid mode: 'static' or 'atr' (dynamic bar-by-bar).",
    )
    parser.add_argument("--grid-size", type=float, help="Decimal percentage for static grid.")
    parser.add_argument("--atr-period", type=int, default=14, help="ATR lookback period.")
    parser.add_argument("--atr-multiplier", type=float, default=1.0, help="ATR multiplier (controls grid step/spacing).")
    parser.add_argument(
        "--atr-window-multiplier",
        type=float,
        default=None,
        help=(
            "ATR mode only. If set, the active grid span shrinks to a volatility-sized "
            "band around the current price -- [price - ATR*mult, price + ATR*mult] -- "
            "clipped to --grid-min/--grid-max, instead of always spanning the full "
            "static range. Independent of --atr-multiplier, which controls level "
            "spacing within whatever span is in effect. Omit to keep the original "
            "behaviour (span is always the full grid_min-grid_max range)."
        ),
    )
    parser.add_argument(
        "--atr-window-sticky",
        action="store_true",
        help=(
            "ATR mode only. Keep a volatility-sized window centered at its "
            "previous center and recenter only when price exits it. Requires "
            "--atr-window-multiplier; otherwise it has no effect."
        ),
    )
    parser.add_argument("--grid-min", type=float, required=True, help="Minimum price bound.")
    parser.add_argument("--grid-max", type=float, required=True, help="Maximum price bound.")
    parser.add_argument("--path", choices=["OLHC", "OHLC"], default="OLHC", help="Intraday path.")
    parser.add_argument(
        "--output-prefix",
        default="grid_backtest",
        help="Filename prefix for the exported '<prefix>_trades.csv' and '<prefix>_equity_curve.csv'.",
    )
    parser.add_argument(
        "--strategy",
        choices=["standard", "every4", "half-split"],
        default="standard",
        help=(
            "Trading strategy variant: 'standard' (buy/sell every level), "
            "'every4' (buy every 4th level, unwind in 4 tranches), or "
            "'half-split' (buy only in lower half of grid, sell only in upper half)."
        ),
    )
    parser.add_argument(
        "--write-csv",
        action="store_true",
        help=(
            "Write the '<prefix>_trades.csv' and '<prefix>_equity_curve.csv' files. "
            "By default no CSV files are written and only the summary is printed."
        ),
    )
    parser.add_argument(
        "--no-initial-assets",
        action="store_true",
        help=(
            "Skip the initial fill and start with 100%% cash / 0%% asset and no "
            "pre-purchased inventory (instead of the default: 100%% cash with an "
            "initial fill that buys just enough asset to back the resting sell "
            "orders above the starting price)."
        ),
    )

    args = parser.parse_args()

    df = load_data(args.csv, args.year, args.atr_period)

    trades, curve, summary = backtest(
        df=df,
        grid_min=args.grid_min,
        grid_max=args.grid_max,
        grid_mode=args.grid_mode,
        grid_size=args.grid_size,
        atr_period=args.atr_period,
        atr_multiplier=args.atr_multiplier,
        atr_window_multiplier=args.atr_window_multiplier,
        atr_window_sticky=args.atr_window_sticky,
        path=args.path,
        strategy=args.strategy,
        no_initial_assets=args.no_initial_assets,
    )

    print_summary(summary, args.year)

    if args.write_csv:
        trades_path = f"{args.output_prefix}_trades.csv"
        curve_path = f"{args.output_prefix}_equity_curve.csv"
        trades.to_csv(trades_path, index=False)
        curve.to_csv(curve_path, index=False)
        print(f"\nTrade log saved to:      {trades_path}")
        print(f"Equity curve saved to:   {curve_path}")


if __name__ == "__main__":
    main()
