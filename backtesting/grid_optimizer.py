#!/usr/bin/env python3
"""Concurrent, multi-year parameter search for ``grid_backtest.py``.

The search applies one parameter set to every available year. Grid bounds are
expressed as multiples of each year's first opening price, which makes a
candidate comparable across the price move from 2020 through 2026.

Candidates are ranked in this order:
1. Beat buy-and-hold in every year (required for the qualified leaderboard).
2. Maximize net realized arbitrage profit across all years.
3. Maximize final portfolio value across all years.

Example:
    python3 grid_optimizer.py --workers 8 --top 20

The defaults search static grids. ATR searches can be enabled with
``--grid-mode atr`` and the corresponding ATR parameter lists.
"""

from __future__ import annotations

import argparse
import itertools
import json
import os
from concurrent.futures import ProcessPoolExecutor
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import pandas as pd

import grid_backtest


DEFAULT_YEARS = (2020, 2021, 2022, 2023, 2024, 2025, 2026)


@dataclass(frozen=True)
class Candidate:
    grid_mode: str
    grid_min_ratio: float
    grid_max_ratio: float
    grid_size: float | None
    atr_multiplier: float | None
    atr_window_multiplier: float | None
    atr_window_sticky: bool
    strategy: str
    no_initial_assets: bool


def parse_list(value: str, convert: Any) -> list[Any]:
    try:
        values = [convert(part.strip()) for part in value.split(",") if part.strip()]
    except ValueError as exc:
        raise argparse.ArgumentTypeError(f"Invalid comma-separated list: {value}") from exc
    if not values:
        raise argparse.ArgumentTypeError("The list must contain at least one value.")
    return values


def make_candidates(args: argparse.Namespace) -> list[Candidate]:
    common = itertools.product(
        args.grid_min_ratios,
        args.grid_max_ratios,
        args.strategies,
        [False, True] if args.test_no_initial_assets else [False],
    )

    candidates: list[Candidate] = []
    if args.grid_mode == "static":
        for minimum, maximum, strategy, no_initial_assets in common:
            if minimum >= maximum:
                continue
            for grid_size in args.grid_sizes:
                candidates.append(
                    Candidate(
                        grid_mode="static",
                        grid_min_ratio=minimum,
                        grid_max_ratio=maximum,
                        grid_size=grid_size,
                        atr_multiplier=None,
                        atr_window_multiplier=None,
                        atr_window_sticky=False,
                        strategy=strategy,
                        no_initial_assets=no_initial_assets,
                    )
                )
    else:
        for minimum, maximum, strategy, no_initial_assets in common:
            if minimum >= maximum:
                continue
            for atr_multiplier, window_multiplier, sticky in itertools.product(
                args.atr_multipliers,
                args.atr_window_multipliers,
                [False, True] if args.test_sticky_window else [False],
            ):
                candidates.append(
                    Candidate(
                        grid_mode="atr",
                        grid_min_ratio=minimum,
                        grid_max_ratio=maximum,
                        grid_size=None,
                        atr_multiplier=atr_multiplier,
                        atr_window_multiplier=window_multiplier,
                        atr_window_sticky=sticky,
                        strategy=strategy,
                        no_initial_assets=no_initial_assets,
                    )
                )
    return candidates


def load_year_data(csv_path: str, year: int, atr_period: int) -> pd.DataFrame:
    return grid_backtest.load_data(csv_path, year, atr_period)


def evaluate_candidate(task: tuple[str, tuple[int, ...], int, str, Candidate]) -> dict[str, Any]:
    csv_path, years, atr_period, path, candidate = task
    yearly: list[dict[str, Any]] = []

    for year in years:
        df = load_year_data(csv_path, year, atr_period)
        start_price = float(df.iloc[0]["Open"])
        summary = grid_backtest.backtest(
            df=df,
            grid_min=start_price * candidate.grid_min_ratio,
            grid_max=start_price * candidate.grid_max_ratio,
            grid_mode=candidate.grid_mode,
            grid_size=candidate.grid_size,
            atr_period=atr_period,
            atr_multiplier=candidate.atr_multiplier,
            atr_window_multiplier=candidate.atr_window_multiplier,
            atr_window_sticky=candidate.atr_window_sticky,
            path=path,
            strategy=candidate.strategy,
            no_initial_assets=candidate.no_initial_assets,
        )[2]
        yearly.append(
            {
                "year": year,
                "final_value": summary["final_value"],
                "total_return_pct": summary["total_return_pct"],
                "buy_hold_return_pct": summary["buy_hold_return_pct"],
                "grid_vs_buy_hold": summary["grid_vs_buy_hold"],
                "grid_vs_buy_hold_pct_points": summary["grid_vs_buy_hold_pct_points"],
                "gross_realized_arb_profit": summary["gross_realized_arb_profit"],
                "net_realized_arb_profit": summary["net_realized_arb_profit"],
                "transaction_costs": summary["transaction_costs"],
                "buys": summary["buys"],
                "sells": summary["sells"],
                "number_of_trades": summary["number_of_trades"],
                "max_drawdown_pct": summary["max_drawdown_pct"],
            }
        )

    versus_buy_hold = [row["grid_vs_buy_hold"] for row in yearly]
    net_arb = sum(row["net_realized_arb_profit"] for row in yearly)
    return {
        "parameters": asdict(candidate),
        "years": yearly,
        "qualified": all(value > 0 for value in versus_buy_hold),
        "min_grid_vs_buy_hold": min(versus_buy_hold),
        "total_grid_vs_buy_hold": sum(versus_buy_hold),
        "total_net_realized_arb_profit": net_arb,
        "total_gross_realized_arb_profit": sum(
            row["gross_realized_arb_profit"] for row in yearly
        ),
        "total_final_value": sum(row["final_value"] for row in yearly),
        "total_trades": sum(row["number_of_trades"] for row in yearly),
    }


def sort_key(result: dict[str, Any]) -> tuple[bool, float, float, float]:
    return (
        result["qualified"],
        result["total_net_realized_arb_profit"],
        result["total_final_value"],
        result["min_grid_vs_buy_hold"],
    )


def print_result(result: dict[str, Any], rank: int) -> None:
    params = result["parameters"]
    print(
        f"{rank:>2}. {'QUALIFIED' if result['qualified'] else 'not qualified':<13} "
        f"net arb EUR {result['total_net_realized_arb_profit']:>12,.2f} | "
        f"min vs B&H EUR {result['min_grid_vs_buy_hold']:>10,.2f} | "
        f"{params['strategy']:<10} min/max {params['grid_min_ratio']:.2f}/{params['grid_max_ratio']:.2f}"
        f" size {params['grid_size'] if params['grid_size'] is not None else 'ATR'}"
        f" {'cash-only' if params['no_initial_assets'] else 'initial-fill'}"
    )
    for year in result["years"]:
        print(
            f"    {year['year']}: grid {year['total_return_pct']:>7.2f}% vs "
            f"B&H {year['buy_hold_return_pct']:>7.2f}% | "
            f"net arb EUR {year['net_realized_arb_profit']:>9,.2f} | "
            f"round-trip sells {year['sells']}"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--csv",
        default="wisdomtree_silver_etc_JE00B1VS3333_price_2020_2026.csv",
        help="Semicolon-separated OHLC CSV.",
    )
    parser.add_argument("--years", default=",".join(map(str, DEFAULT_YEARS)))
    parser.add_argument("--grid-mode", choices=["static", "atr"], default="static")
    parser.add_argument("--grid-min-ratios", default="0.35,0.5,0.65")
    parser.add_argument("--grid-max-ratios", default="2,3,4,5")
    parser.add_argument("--grid-sizes", default="0.02,0.03,0.04,0.05,0.058,0.07,0.09")
    parser.add_argument("--atr-multipliers", default="0.5,1,2,3")
    parser.add_argument("--atr-window-multipliers", default="10,25,50")
    parser.add_argument("--atr-period", type=int, default=14)
    parser.add_argument("--strategies", default="standard,every4,half-split")
    parser.add_argument("--path", choices=["OLHC", "OHLC"], default="OLHC")
    parser.add_argument("--workers", type=int, default=os.cpu_count() or 1)
    parser.add_argument("--top", type=int, default=10, help="Number of results to display.")
    parser.add_argument("--test-no-initial-assets", action="store_true")
    parser.add_argument("--test-sticky-window", action="store_true")
    parser.add_argument("--output", type=Path, help="Optional JSON output path.")
    args = parser.parse_args()

    args.grid_min_ratios = parse_list(args.grid_min_ratios, float)
    args.grid_max_ratios = parse_list(args.grid_max_ratios, float)
    args.grid_sizes = parse_list(args.grid_sizes, float)
    args.atr_multipliers = parse_list(args.atr_multipliers, float)
    args.atr_window_multipliers = parse_list(args.atr_window_multipliers, float)
    args.strategies = parse_list(args.strategies, str)
    years = tuple(parse_list(args.years, int))
    csv_path = str(Path(args.csv).resolve())
    candidates = make_candidates(args)
    if not candidates:
        parser.error("No valid parameter combinations were generated.")

    task_args = (csv_path, years, args.atr_period, args.path)
    tasks = [(task_args[0], task_args[1], task_args[2], task_args[3], candidate) for candidate in candidates]
    print(f"Testing {len(tasks):,} candidates across {len(years)} years with {args.workers} workers...")
    with ProcessPoolExecutor(max_workers=args.workers) as executor:
        results = list(executor.map(evaluate_candidate, tasks))

    results.sort(key=sort_key, reverse=True)
    qualified_count = sum(result["qualified"] for result in results)
    print(f"Completed. {qualified_count:,} candidates beat buy-and-hold in every year.")
    print("\nLeaderboard (qualified candidates rank first; net realized arb is primary):")
    for rank, result in enumerate(results[: args.top], start=1):
        print_result(result, rank)

    if args.output:
        args.output.write_text(json.dumps(results, indent=2), encoding="utf-8")
        print(f"\nFull results written to {args.output}")


if __name__ == "__main__":
    main()