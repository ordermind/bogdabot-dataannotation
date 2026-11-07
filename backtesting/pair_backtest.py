#!/usr/bin/env python3
"""
Long-only relative-strength backtester for two securities, using a rolling
hedge ratio and either a rolling z-score or percentage difference.

Input:
    Two semicolon-separated CSVs, one per security, each with columns:
        Date;Open;High;Low;Close;Volume
    (same format grid_backtest.py expects). The two files are merged on Date
    (inner join), so only dates present in both series are used.

Signal construction (all computed causally, no look-ahead):
    1. Rolling hedge ratio (beta) of security A on security B, over the
       trailing --hedge-lookback bars:
           beta_t = RollingCov(Close_A, Close_B, lookback) / RollingVar(Close_B, lookback)
    2. Spread:
           spread_t = Close_A[t] - beta_t * Close_B[t]
    3. Signal construction depends on --mode:
       z: Rolling z-score of the spread over the trailing --zscore-window bars:
           z_t = (spread_t - RollingMean(spread, window)) / RollingStd(spread, window)
       pct: Percentage residual from the beta-adjusted B price:
            pct_t = spread_t / (beta_t * Close_B[t]) * 100
    4. Both beta and z are then shifted forward one bar (Beta_Prior /
       Zscore_Prior) so that the signal used to decide trading on bar t is
       entirely built from information available at the close of bar t-1 --
       exactly the same look-ahead discipline grid_backtest.py applies to its
       ATR_Prior. Trades execute at bar t's Open.

Strategy (long-only):
    When the selected signal is below the negative entry threshold, hold 100% in A
    because A is unusually cheap relative to B. When it is above the entry
    threshold, hold 100% in B because B is unusually cheap relative to A.
    When an existing one-asset position returns inside the exit threshold,
    rebalance to 50% A / 50% B. No short positions are opened.

Allocation:
    Outside the exit range, all available capital is allocated to the relatively
    cheaper asset. Inside the exit range, the portfolio is rebalanced to 50% A / 50% B.

Examples:
    Full available history, default thresholds:
        python3 pair_backtest.py --csv-a invesco.csv --csv-b ishares.csv

    Single year, tighter entry/exit, CSVs exported:
        python3 pair_backtest.py --csv-a invesco.csv --csv-b ishares.csv \\
            --year 2024 --entry-z 1.5 --exit-z 0.25 \\
            --write-csv --output-prefix pair_2024
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


@dataclass
class Trade:
    date: str
    leg: str            # "A" or "B"
    side: str            # BUY, SELL
    price: float
    quantity: float
    gross_value: float
    transaction_cost: float
    cash_after: float
    qty_a_after: float
    qty_b_after: float
    signal: float
    action: str          # A_ONLY, B_ONLY, or BALANCED allocation


@dataclass
class RoundTrip:
    entry_date: str
    exit_date: str
    position: str        # A_ONLY, B_ONLY, or BALANCED
    entry_signal: float
    exit_signal: float
    exit_reason: str      # EQUALIZE or SWITCH
    days_held: int
    pnl: float


def transaction_cost(value: float) -> float:
    return max(value * TRANSACTION_RATE, MIN_TRANSACTION_COST)


def load_series(filename: str) -> pd.DataFrame:
    """Load one security's OHLC CSV, same format/parsing as grid_backtest.py."""
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
        raise ValueError(f"{filename} is missing columns: {sorted(missing)}")

    df["Date"] = pd.to_datetime(df["Date"], format="%A, %B %d, %Y")
    for column in ["Open", "High", "Low", "Close"]:
        df[column] = pd.to_numeric(df[column], errors="coerce")

    df = df.dropna(subset=["Date", "Open", "High", "Low", "Close"]).sort_values("Date").reset_index(drop=True)
    return df[["Date", "Open", "High", "Low", "Close"]]


def load_pair(csv_a: str, csv_b: str, year: int | None) -> pd.DataFrame:
    """Load both legs and inner-join them on Date so only common trading days remain."""
    df_a = load_series(csv_a)
    df_b = load_series(csv_b)

    merged = pd.merge(df_a, df_b, on="Date", how="inner", suffixes=("_A", "_B"))
    merged = merged.sort_values("Date").reset_index(drop=True)
    if merged.empty:
        raise ValueError("The two CSVs share no common dates after merging.")

    return merged, year


def compute_signals(df: pd.DataFrame, hedge_lookback: int, zscore_window: int, mode: str) -> pd.DataFrame:
    """Add the selected signal and its one-bar-lagged counterpart."""
    if mode not in {"z", "pct"}:
        raise ValueError("mode must be 'z' or 'pct'.")
    if hedge_lookback < 2:
        raise ValueError("hedge_lookback must be >= 2.")
    if zscore_window < 2:
        raise ValueError("zscore_window must be >= 2.")

    cov = df["Close_A"].rolling(hedge_lookback).cov(df["Close_B"])
    var = df["Close_B"].rolling(hedge_lookback).var()
    beta = cov / var

    spread = df["Close_A"] - beta * df["Close_B"]
    mean = spread.rolling(zscore_window).mean()
    std = spread.rolling(zscore_window).std()
    z = (spread - mean) / std
    pct = spread / (beta * df["Close_B"]) * 100

    df = df.copy()
    df["Beta"] = beta
    df["Spread"] = spread
    df["Zscore"] = z
    df["PctDifference"] = pct
    # Shift the whole derived signal chain by one bar: Zscore_Prior[t] is exactly
    # what was known at the close of bar t-1 (itself only built from bars <= t-1),
    # so trading on bar t's Open using Zscore_Prior[t] involves no look-ahead.
    df["Beta_Prior"] = beta.shift(1)
    df["Zscore_Prior"] = z.shift(1)
    df["PctDifference_Prior"] = pct.shift(1)
    df["Signal"] = z if mode == "z" else pct
    df["Signal_Prior"] = df["Signal"].shift(1)
    return df


def filter_year(df: pd.DataFrame, year: int | None) -> pd.DataFrame:
    if year is None:
        return df
    out = df[df["Date"].dt.year == year].reset_index(drop=True)
    if out.empty:
        raise ValueError(f"No overlapping data found for year {year}.")
    return out


def backtest(
    df: pd.DataFrame,
    mode: str,
    entry_threshold: float,
    exit_threshold: float,
) -> tuple[pd.DataFrame, pd.DataFrame, pd.DataFrame, dict]:
    if entry_threshold <= exit_threshold:
        raise ValueError("Entry threshold must be greater than exit threshold.")

    cash = INITIAL_CAPITAL
    qty_a = 0.0
    qty_b = 0.0
    position = "CASH"
    entry_date: str | None = None
    entry_signal: float | None = None
    entry_cash_snapshot = cash

    trades: list[Trade] = []
    round_trips: list[RoundTrip] = []
    equity_curve = []

    def execute_leg(leg: str, side: str, price: float, quantity: float, date: str, signal: float, action: str) -> None:
        nonlocal cash, qty_a, qty_b
        if quantity <= 0 or price <= 0:
            return
        gross = price * quantity
        cost = transaction_cost(gross)
        if side == "BUY":
            cash -= gross + cost
            if leg == "A":
                qty_a += quantity
            else:
                qty_b += quantity
        else:
            cash += gross - cost
            if leg == "A":
                qty_a -= quantity
            else:
                qty_b -= quantity

        trades.append(
            Trade(
                date=date,
                leg=leg,
                side=side,
                price=price,
                quantity=quantity,
                gross_value=gross,
                transaction_cost=cost,
                cash_after=cash,
                qty_a_after=qty_a,
                qty_b_after=qty_b,
                signal=signal,
                action=action,
            )
        )

    def buy_value(leg: str, price: float, value: float, date: str, signal: float, action: str) -> None:
        available_value = max(cash, 0.0)
        gross_value = min(value, available_value / (1 + TRANSACTION_RATE))
        execute_leg(leg, "BUY", price, gross_value / price, date, signal, action)

    def rebalance(target_a: float, target_b: float, open_a: float, open_b: float, date: str, signal: float, action: str) -> None:
        nonlocal cash, qty_a, qty_b
        execute_leg("A", "SELL", open_a, qty_a, date, signal, action)
        execute_leg("B", "SELL", open_b, qty_b, date, signal, action)
        total_value = cash
        buy_value("A", open_a, total_value * target_a, date, signal, action)
        buy_value("B", open_b, total_value * target_b, date, signal, action)

    for _, row in df.iterrows():
        date = row["Date"].strftime("%Y-%m-%d")
        open_a, open_b = float(row["Open_A"]), float(row["Open_B"])
        signal_prior = row["Signal_Prior"]

        if position == "CASH":
            target_position = "BALANCED"
            if pd.notna(signal_prior):
                if signal_prior <= -entry_threshold:
                    target_position = "A_ONLY"
                elif signal_prior >= entry_threshold:
                    target_position = "B_ONLY"
        elif pd.notna(signal_prior):
            target_position = position
            if signal_prior <= -entry_threshold:
                target_position = "A_ONLY"
            elif signal_prior >= entry_threshold:
                target_position = "B_ONLY"
            elif abs(signal_prior) <= exit_threshold:
                target_position = "BALANCED"
        else:
            target_position = position

        if target_position != position:
            if position in {"A_ONLY", "B_ONLY"} and entry_date is not None:
                round_trips.append(
                    RoundTrip(
                        entry_date=entry_date,
                        exit_date=date,
                        position=position,
                        entry_signal=entry_signal,
                        exit_signal=signal_prior,
                        exit_reason="EQUALIZE" if target_position == "BALANCED" else "SWITCH",
                        days_held=(pd.Timestamp(date) - pd.Timestamp(entry_date)).days,
                        pnl=cash + qty_a * open_a + qty_b * open_b - entry_cash_snapshot,
                    )
                )

            target_a = 1.0 if target_position == "A_ONLY" else 0.0 if target_position == "B_ONLY" else 0.5
            target_b = 1.0 - target_a
            rebalance(target_a, target_b, open_a, open_b, date, signal_prior, target_position)
            if target_position in {"A_ONLY", "B_ONLY"}:
                entry_date, entry_signal = date, signal_prior
                entry_cash_snapshot = cash + qty_a * open_a + qty_b * open_b
            position = target_position

        close_a, close_b = float(row["Close_A"]), float(row["Close_B"])
        equity_curve.append(
            {
                "Date": row["Date"],
                "Close_A": close_a,
                "Close_B": close_b,
                "Cash": cash,
                "Qty_A": qty_a,
                "Qty_B": qty_b,
                "Beta": row["Beta"],
                "Zscore": row["Zscore"],
                "Signal": signal_prior,
                "Position": position,
                "PortfolioValue": cash + qty_a * close_a + qty_b * close_b,
            }
        )

    trades_df = pd.DataFrame(asdict(t) for t in trades)
    round_trips_df = pd.DataFrame(asdict(r) for r in round_trips)
    curve_df = pd.DataFrame(equity_curve)

    final_value = float(curve_df.iloc[-1]["PortfolioValue"])
    total_costs = sum(t.transaction_cost for t in trades)

    start_row = df.iloc[0]
    final_row = df.iloc[-1]
    bh_units_a = (INITIAL_CAPITAL / 2) / float(start_row["Open_A"])
    bh_units_b = (INITIAL_CAPITAL / 2) / float(start_row["Open_B"])
    buy_hold_final = bh_units_a * float(final_row["Close_A"]) + bh_units_b * float(final_row["Close_B"])

    curve_df["Peak"] = curve_df["PortfolioValue"].cummax()
    curve_df["Drawdown"] = curve_df["PortfolioValue"] / curve_df["Peak"] - 1
    max_drawdown = curve_df["Drawdown"].min()

    wins = sum(r.pnl > 0 for r in round_trips)
    losses = sum(r.pnl <= 0 for r in round_trips)
    win_rate = (wins / len(round_trips) * 100) if round_trips else 0.0

    summary = {
        "start_date": start_row["Date"].strftime("%Y-%m-%d"),
        "end_date": final_row["Date"].strftime("%Y-%m-%d"),
        "bars": len(df),
        "mode": mode,
        "entry_threshold": entry_threshold,
        "exit_threshold": exit_threshold,
        "beta_min": curve_df["Beta"].min(),
        "beta_max": curve_df["Beta"].max(),
        "beta_mean": curve_df["Beta"].mean(),
        "start_price_a": float(start_row["Open_A"]),
        "start_price_b": float(start_row["Open_B"]),
        "end_price_a": float(final_row["Close_A"]),
        "end_price_b": float(final_row["Close_B"]),
        "final_cash": cash,
        "final_value": final_value,
        "total_profit_loss": final_value - INITIAL_CAPITAL,
        "total_return_pct": (final_value / INITIAL_CAPITAL - 1) * 100,
        "transaction_costs": total_costs,
        "number_of_trades": len(trades),
        "number_of_round_trips": len(round_trips),
        "wins": wins,
        "losses": losses,
        "win_rate_pct": win_rate,
        "avg_pnl_per_round_trip": (sum(r.pnl for r in round_trips) / len(round_trips)) if round_trips else 0.0,
        "avg_days_held": (sum(r.days_held for r in round_trips) / len(round_trips)) if round_trips else 0.0,
        "buy_hold_final": buy_hold_final,
        "buy_hold_return_pct": (buy_hold_final / INITIAL_CAPITAL - 1) * 100,
        "pairs_vs_buy_hold": final_value - buy_hold_final,
        "pairs_vs_buy_hold_pct_points": (final_value / INITIAL_CAPITAL - 1) * 100
        - (buy_hold_final / INITIAL_CAPITAL - 1) * 100,
        "max_drawdown_pct": max_drawdown * 100,
    }

    return trades_df, round_trips_df, curve_df, summary


def print_summary(summary: dict, label_a: str, label_b: str) -> None:
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

    print(f"\n=== PAIR TRADING BACKTEST: {label_a} vs {label_b} ===")
    print(f"Period:                  {summary['start_date']} to {summary['end_date']} ({summary['bars']} bars)")
    threshold_label = "z-score" if summary["mode"] == "z" else "% difference"
    print(f"Entry / Exit {threshold_label}: {summary['entry_threshold']} / {summary['exit_threshold']}")
    print("Allocation:              100% relatively cheaper asset, or 50/50 inside exit range")
    print(
        f"Hedge ratio (beta):      min {summary['beta_min']:.3f} / mean {summary['beta_mean']:.3f} / max {summary['beta_max']:.3f}"
    )
    print(f"Start prices:            {label_a} \u20ac{summary['start_price_a']:.2f}  |  {label_b} \u20ac{summary['start_price_b']:.2f}")
    print(f"End prices:              {label_a} \u20ac{summary['end_price_a']:.2f}  |  {label_b} \u20ac{summary['end_price_b']:.2f}")
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
    print("--- ROUND-TRIP PERFORMANCE ---")
    print(f"Round trips:             {summary['number_of_round_trips']} ({summary['wins']} win / {summary['losses']} loss, {summary['win_rate_pct']:.1f}% win rate)")
    print(f"Avg PnL per round trip:  \u20ac{summary['avg_pnl_per_round_trip']:,.2f}")
    print(f"Avg days held:           {summary['avg_days_held']:.1f}")
    print(f"Total transaction fees:  \u20ac{summary['transaction_costs']:,.2f}")
    print(f"Total trades (legs):     {summary['number_of_trades']}")
    print()
    print(f"Buy & hold (50/50) value: \u20ac{summary['buy_hold_final']:,.2f}")
    print(f"Buy & hold return:        {summary['buy_hold_return_pct']:.2f}%")

    vs_bh_positive = summary["pairs_vs_buy_hold"] >= 0
    vs_bh_line = (
        f"VS BUY & HOLD: \u20ac{signed(summary['pairs_vs_buy_hold'])} "
        f"({signed(summary['pairs_vs_buy_hold_pct_points'])} pp)"
    )
    border = "=" * (len(vs_bh_line) + 4)
    print(border)
    print(f"  {highlight(vs_bh_line, vs_bh_positive)}")
    print(border)
    print()
    print(f"Maximum drawdown:         {summary['max_drawdown_pct']:.2f}%")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Pair-trading (spread mean-reversion) backtester for two securities.",
        epilog=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--csv-a", required=True, help="Path to security A's CSV.")
    parser.add_argument("--csv-b", required=True, help="Path to security B's CSV.")
    parser.add_argument("--label-a", default=None, help="Display name for security A (default: filename stem).")
    parser.add_argument("--label-b", default=None, help="Display name for security B (default: filename stem).")
    parser.add_argument("--year", type=int, default=None, help="Restrict the backtest to one year (default: full overlapping history).")
    parser.add_argument("--mode", choices=["z", "pct"], default="z", help="Signal mode: statistical z-score or percentage difference (default: z).")
    parser.add_argument("--hedge-lookback", type=int, default=60, help="Rolling window (bars) for the hedge-ratio (beta) regression.")
    parser.add_argument("--zscore-window", type=int, default=20, help="Rolling window (bars) for the spread z-score.")
    parser.add_argument("--entry-z", type=float, default=2.0, help="Enter a position when |z| >= this value.")
    parser.add_argument("--exit-z", type=float, default=0.5, help="Exit a position when |z| <= this value.")
    parser.add_argument("--entry-pct", type=float, default=2.0, help="Percentage mode entry threshold (default: 2.0%%).")
    parser.add_argument("--exit-pct", type=float, default=0.5, help="Percentage mode exit threshold (default: 0.5%%).")
    parser.add_argument("--output-prefix", default="pair_backtest", help="Filename prefix for exported CSVs.")
    parser.add_argument("--write-csv", action="store_true", help="Write '<prefix>_trades.csv', '<prefix>_round_trips.csv' and '<prefix>_equity_curve.csv'.")

    args = parser.parse_args()

    label_a = args.label_a or Path(args.csv_a).stem
    label_b = args.label_b or Path(args.csv_b).stem

    merged, year = load_pair(args.csv_a, args.csv_b, args.year)
    # Signals computed on the full merged history *before* the year filter, so bars
    # near the start of the requested year still see a fully warmed-up rolling
    # window instead of losing their look-back to the filter boundary (same
    # rationale as ATR_Prior in grid_backtest.py).
    signaled = compute_signals(merged, args.hedge_lookback, args.zscore_window, args.mode)
    df = filter_year(signaled, year)

    if args.mode == "z":
        entry_threshold, exit_threshold = args.entry_z, args.exit_z
    else:
        entry_threshold, exit_threshold = args.entry_pct, args.exit_pct

    trades, round_trips, curve, summary = backtest(
        df=df,
        mode=args.mode,
        entry_threshold=entry_threshold,
        exit_threshold=exit_threshold,
    )

    print_summary(summary, label_a, label_b)

    if args.write_csv:
        trades_path = f"{args.output_prefix}_trades.csv"
        round_trips_path = f"{args.output_prefix}_round_trips.csv"
        curve_path = f"{args.output_prefix}_equity_curve.csv"
        trades.to_csv(trades_path, index=False)
        round_trips.to_csv(round_trips_path, index=False)
        curve.to_csv(curve_path, index=False)
        print(f"\nTrade log saved to:      {trades_path}")
        print(f"Round trips saved to:    {round_trips_path}")
        print(f"Equity curve saved to:   {curve_path}")


if __name__ == "__main__":
    main()
