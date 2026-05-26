# ICICI Export

The ICICI salary export creates an `.xlsx` bulk-payment sheet with `PAB_VENDOR` rows.

## Settings

The debit account number is saved in `settings.json` beside the running app. The export dialog asks for:

- ICICI debit account number, required, 6 to 30 digits.
- Payment date, required, defaulting to today.

Only the debit account number is persisted. The payment date is chosen for each export.

## Row Rules

Cash employees are excluded. ICICI employees export with payment mode `FT`; other bank employees export with `NEFT`. Account number and IFSC must pass validation, and payable salary must be greater than zero.

Payroll validation runs before export. The export is blocked if attendance, deductions, or advance deductions would produce invalid or negative payable salary.
