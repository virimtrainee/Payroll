# Employee Import Format

Imports read the first worksheet of an `.xlsx` file. The first used row is treated as the header row. Header matching is case-insensitive.

## Required Columns

- Name: `BNF_NAME`, `NAME`, `EMPLOYEE_NAME`, or `EMPLOYEE`
- Salary: `AMOUNT`, `SALARY`, `BASE_SALARY`, or `NET_SALARY`

## Optional Columns

- Account number: `BENE_ACC_NO`, `ACCOUNT_NO`, `ACCOUNT`, `ACC_NO`, or `ACCOUNT_NUMBER`
- IFSC: `BENE_IFSC`, `IFSC`, or `IFSC_CODE`
- Payment mode: `PYMT_MODE`, `PAYMENT_MODE`, `MODE`, or `BANK_TYPE`
- Aadhar number: `AADHAR_NUMBER`, `AADHAAR_NUMBER`, `AADHAR`, or `AADHAAR`
- UAN number: `UAN_NUMBER` or `UAN`
- Insurance number: `INSURANCE_NUMBER`, `INSURANCE_NO`, or `INSURANCE`
- Phone number: `PHONE_NUMBER`, `PHONE`, `MOBILE_NUMBER`, or `MOBILE`

Payment mode accepts `CASH`, `FT`, `ICICI`, `ICICI BANK`, `NEFT`, `OTHER`, and `OTHER BANK`. If mode is blank, rows with no bank details are treated as cash; rows with an `ICIC0...` IFSC are treated as ICICI; other bank rows are treated as NEFT/OtherBank.

## Validation

The import aborts when workbook rows contain validation errors. Existing database duplicates are skipped and reported after import; duplicate names inside the workbook are row errors. Salary cells must be numeric or parseable as a number. Invalid rows are reported with row numbers.
