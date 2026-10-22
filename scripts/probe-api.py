#!/usr/bin/env python3
"""Probe the API for the two date formats it accepts, then clean up.

Usage:
    python3 scripts/probe-api.py [--api https://fmapi.muhit.dev] [--email ...] [--password ...]

Why this exists
---------------
`Expenses.ExpenseDate` is `timestamp with time zone`. Npgsql refuses to write
a `DateTime` whose Kind is not Utc, and a JSON date-only string deserialises
with Kind=Unspecified. So:

    "2026-10-22"                 -> 500 (Kind=Unspecified)
    "2026-10-22T00:00:00.000Z"   -> 200 (Kind=Utc)

The API normalises an Unspecified date server-side, so a healthy API answers
200 to both. This script tells you which build is actually running, and it
deletes every row it creates.
"""

import argparse
import datetime
import os
import sys

import requests

DEFAULT_API = os.environ.get("API_URL", "https://fmapi.muhit.dev")


def login(api: str, email: str, password: str) -> str:
    r = requests.post(f"{api}/api/Authentication/login",
                      json={"email": email, "password": password}, timeout=60)
    if r.status_code != 200:
        print(f"login failed: {r.status_code} {r.text[:200]}", file=sys.stderr)
        raise SystemExit(1)
    return r.json()["token"]


def probe(api: str, headers: dict, cause: str, expense_date: str) -> int:
    payload = {
        "cause": cause,
        "amount": 1,
        "quantity": 1,
        "unitPrice": 1,
        "expenseDate": expense_date,
        "expenseCategoryId": 1,
    }
    r = requests.post(f"{api}/api/Expense/Create", headers=headers, json=payload, timeout=60)
    return r.status_code


def cleanup(api: str, headers: dict, cause: str) -> None:
    rows = requests.get(f"{api}/api/Expense/GetAll", headers=headers, timeout=60).json()
    for row in rows:
        if (row.get("cause") or "") == cause:
            d = requests.delete(f"{api}/api/Expense/Delete/{row['id']}",
                                headers=headers, timeout=60)
            print(f"  cleaned up expense {row['id']} -> {d.status_code}")


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--api", default=DEFAULT_API)
    p.add_argument("--email", default=os.environ.get("API_EMAIL", "admin@gmail.com"))
    p.add_argument("--password", default=os.environ.get("API_PASSWORD", "123456"))
    p.add_argument("--keep", action="store_true", help="do not delete the probe rows")
    args = p.parse_args()

    token = login(args.api, args.email, args.password)
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    print(f"logged in to {args.api}")

    today = datetime.date.today().isoformat()
    cases = [
        ("bare date (yyyy-MM-dd)", today),
        ("ISO UTC instant", f"{today}T00:00:00.000Z"),
    ]

    results = {}
    for label, value in cases:
        cause = f"PROBE {label}"
        status = probe(args.api, headers, cause, value)
        results[label] = status
        print(f"  {label:26} -> {status}")
        if not args.keep:
            cleanup(args.api, headers, cause)

    print()
    if results["bare date (yyyy-MM-dd)"] == 200:
        print("API is healthy: it normalises an Unspecified date server-side.")
    else:
        print("API is still running the old build (bare date -> 500).")
        print("It will accept a UTC instant until the new image is deployed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
