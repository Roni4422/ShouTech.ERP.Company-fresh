# Phase 7 — Ribbon Windows Batch 1

**Date:** 2026-10-13
**Goal:** stop the ribbon from falling through to the generic module workspace
for shell-level actions, and give those actions real, modern, friendly windows.

## Shipped

| Action key | Window | Notes |
|---|---|---|
| Tools.Calculator | CalcWin | Full calculator: keyboard input, memory keys, division-by-zero guard, live history list |
| Tools.Keyboard | KbdWin | On-screen keyboard with EN / AR / FR layouts, space, backspace, newline, copy |
| Help.About | AbtWin | Branded About dialog replacing the old MessageBox |
| Help.Instructions | HlpWin | Offline quick guide: topic list + content panel |
| Help.Suggestions | FdbWin | Structured feedback composer with copy-to-clipboard |
| View.InfoWindow | InfoWin | Live session/environment card + shortcut reference |

## Architecture

- RibbonWinDisp (Views) is the single key-to-window dispatcher. It is called
  from MainShVM.TryHandleSystemAction before the generic module navigation,
  so window wiring stays out of MWin.
- All windows derive from BaseWin and inherit the shell contract:
  live re-localization, RTL/LTR flow, theme brushes, geometry persistence,
  company/user context.
- Windows are code-built (no XAML) following the existing PosWin / BnkCard
  pattern, use only DynamicResource theme brushes, and reuse a shared
  single-instance show helper.

## Design rules for future windows

1. Rounded cards (CornerRadius 10-14), ShellContentBackgroundBrush +
   ShellContentBorderBrush, accent buttons via AccentPrimary.
2. Every user-visible string goes through a localization key with an inline
   fallback so missing keys never leak to the UI.
3. One window type per ribbon action; single instance per window class.
4. Nothing leaves the machine automatically (offline-first product rule).

## Roadmap — next batches (highest value first)

- Banks: estimated bank statement, bank reconciliation, bank balances.
- Customers: customer prices, discounts, areas, dues follow-up.
- Bonds/Vouchers: receipt, payment, journal voucher screens with
  fiscal-period validation through AppHost.AccountingStructure.
- Orders: purchase/sales order tracking + payments.
- SalesRep: rep card, targets, collections.
- Manufacturing: templates and operations on top of MfgWin.
- Exit tab: lock screen and company switch UX.

## Validation status

The .NET SDK was not available in the authoring environment, so a compile +
manual pass over the new windows remains required before merging, as with
previous phases.
