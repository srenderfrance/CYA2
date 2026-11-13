# Administrator account and user imports

These are server-side imports, not browser uploads. An administrator must place each workbook on the application host, open the Admin page, load its preview, review all rows, and select the corresponding Create button. The first worksheet is used. Place exactly one `.xlsx` file directly in each folder; subfolders are not searched. Each workbook is limited to 10 MB, 10,000 data rows, and 256 columns.

The folders are under the web application's content root:

- `AccountImports` for primary accounts, typically `src/Cya2.Web/AccountImports` when running the web project locally.
- `UserImports` for users, typically `src/Cya2.Web/UserImports` when running the web project locally.

On a deployed host, use its configured content root, not the administrator's workstation. Create the folders there if needed, restrict filesystem access to authorized operators and the application identity, transfer workbooks securely, and remove them securely after use. Folder contents are excluded from publish output. UserImports contents and account Excel workbooks are ignored by Git; Git ignore does not remove previously tracked files. Do not place these files under `wwwroot` or share them publicly.

## Accounts

Use one header row with `fund`, `fund#`, and `overhead` columns. The `class` column is optional and its values may be blank. `fund#` is the account number. Store numbers with leading zeros as text in Excel. Overhead uses whole-number percentage units (for example, `12` means 12%); a properly formatted Excel percentage such as `12%` is also accepted. Fund and fund# must be unique within the file and against existing accounts; supplied class values must also be unique. Blank classes are stored as NULL. The import creates only primary accounts, with empty Soft Credit and zero Balance Adjustment.

## Users

Use one header row with `User` and `Level` columns. User is an organizational email in `firstname.lastname@scene8.net` form. Names are derived from the two email components. Level is case-insensitive: `admin` creates an Admin, `normal` creates a User, and `view all` creates a Viewer. Users are created without linked accounts or a default account; assign account access manually afterward. Intern users are not supported by this import.

## Validation and recovery

A file with invalid rows, duplicates, existing values, or ambiguous/missing required headers cannot be imported. Correct the workbook and load a new preview. The reviewed rows, rather than a subsequently replaced workbook, are used for confirmation. Each import executes its database writes in a single transaction. If a database conflict occurs after preview, the batch is rejected rather than partially imported. User creation writes a transactional audit record for each created user.

If the application reports that the result could not be confirmed, inspect the account or user list before retrying: a connection interruption can make the transaction outcome uncertain. If the database is unavailable, the application remains in its normal limited mode and imports cannot run.
