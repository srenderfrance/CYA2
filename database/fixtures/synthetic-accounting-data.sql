-- CYA2 synthetic accounting edge-case fixture.
-- Fictional data only. Run after database/schema/001_InitialSchema.sql
-- against an isolated development database. It removes only its own fixture
-- rows before inserting the data so it can be rerun safely.

SET @fixture_class = 'General Administration:Fundraising:General Giving';
SET @fixture_fund = 'Synthetic Accounting';

DELETE FROM AccountingData
WHERE Num LIKE 'ACC-SYNTH-%';

DELETE FROM Accounts
WHERE Fund = @fixture_fund
  AND AccountNumber = '2200000';

INSERT INTO Accounts
	(Fund, AccountingClass, AccountNumber, Overhead, SoftCredit, BalanceAdjustment, OtherFunds)
VALUES
	(@fixture_fund, @fixture_class, '2200000', 0.00, '', 0.00, FALSE);

INSERT INTO AccountingData
	(AccountingClass, Date, Num, Amount, AccountNumber, Account, Type)
VALUES
	-- Ordinary expense: classified as Expense and subtracted from balance.
	(@fixture_class, '2025-01-05 12:00:00', 'ACC-SYNTH-001', 100.00, '6001', 'Office Supplies', 'Expense'),

	-- Payroll Check: classified as Expense even without an expense account prefix.
	(@fixture_class, '2025-01-06 12:00:00', 'ACC-SYNTH-002', 200.00, '6002', 'Payroll', 'Payroll Check'),

	-- Account-prefix expense: classified as Expense by account name.
	(@fixture_class, '2025-01-07 12:00:00', 'ACC-SYNTH-003', 50.00, '6003', 'Expenses: Travel', 'Check'),

	-- Positive and negative transfers retain their signs in TransferTotal.
	(@fixture_class, '2025-01-08 12:00:00', 'ACC-SYNTH-004', 300.00, '7001', 'Transfer: Incoming', 'Journal'),
	(@fixture_class, '2025-01-09 12:00:00', 'ACC-SYNTH-005', -75.00, '7002', 'Transfer: Outgoing', 'Journal'),

	-- Other transaction: included in balance but neither expense nor transfer.
	(@fixture_class, '2025-01-10 12:00:00', 'ACC-SYNTH-006', 40.00, '8001', 'Income', 'Deposit'),

	-- General giving 2200000 is included in balance but excluded from both categories.
	(@fixture_class, '2025-01-11 12:00:00', 'ACC-SYNTH-007', 25.00, '2200000', '2200000 Unrestricted:General', 'Journal'),

	-- These accounts are excluded before classification and balance calculation.
	(@fixture_class, '2025-01-12 12:00:00', 'ACC-SYNTH-008', 90.00, '9001', 'Prepaids', 'Asset'),
	(@fixture_class, '2025-01-13 12:00:00', 'ACC-SYNTH-009', 80.00, '9002', 'Payroll Clearing Insurance', 'Asset'),

	-- Expense wins over transfer when both rules match.
	(@fixture_class, '2025-01-14 12:00:00', 'ACC-SYNTH-010', 60.00, '6004', 'Transfer: Supplies', 'Expense'),

	-- Different account number but same class: included by normal account matching.
	(@fixture_class, '2025-01-15 12:00:00', 'ACC-SYNTH-011', 30.00, '9999', 'Other Class-Matched Item', 'Deposit'),

	-- Different class and account number: excluded when matching a normal account.
	('Other Test Class', '2025-01-16 12:00:00', 'ACC-SYNTH-012', 999.00, '9999', 'Other Class-Mismatched Item', 'Expense');
