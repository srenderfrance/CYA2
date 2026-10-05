-- CYA2 synthetic donor edge-case fixture.
-- Fictional data only. Run after database/schema/001_InitialSchema.sql
-- against an isolated development database. It removes only its own fixture
-- rows before inserting the data so it can be rerun safely.

SET @fixture_fund = 'Test Fund';
SET @intern_fund = 'Intern: Synthetic Intern';
SET @separate_subfund = 'Synthetic Separate Fund';
SET @merged_subfund = 'Synthetic Merged Fund';

-- Remove only rows owned by this fixture so the script can be rerun safely in
-- an isolated development database. Do not run this script against production.
DELETE FROM DonationData
WHERE GiftImportId LIKE 'SYNTH-DONOR-%';

DELETE FROM SubAccounts
WHERE SubFund IN (@separate_subfund, @merged_subfund);

DELETE FROM Accounts
WHERE AccountNumber IN ('SYNTH-ACCOUNT-001', 'SYNTH-INTERN-001');

INSERT INTO Accounts
	(Fund, AccountingClass, AccountNumber, Overhead, SoftCredit, BalanceAdjustment, OtherFunds)
VALUES
	(@fixture_fund, 'SyntheticData:Donations:General', 'SYNTH-ACCOUNT-001', 0.00, '', 0.00, FALSE),
	(@intern_fund, 'SyntheticData:Donations:General', 'SYNTH-INTERN-001', 0.00, '', 0.00, FALSE);

SET @primary_account_id = (SELECT AccountId FROM Accounts WHERE Fund = @fixture_fund LIMIT 1);

INSERT INTO SubAccounts
	(AccountId, SubFund, Kind)
VALUES
	(@primary_account_id, @separate_subfund, 'Separate'),
	(@primary_account_id, @merged_subfund, 'Merged');

DELETE FROM DonorContacts
WHERE DonorId BETWEEN 900001 AND 900013;

DELETE FROM Donors
WHERE Id BETWEEN 900001 AND 900013
  AND Fund IN (@fixture_fund, @intern_fund);

INSERT INTO Donors
	(Id, Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion)
VALUES
	(900001, @fixture_fund, 'Donor1', 'donor1', 'AccountName', 1),
	(900002, @fixture_fund, 'Donor2 Primary', 'donor2primary', 'PrimaryAddressee', 1),
	(900003, @fixture_fund, 'Donor3 ThirdParty', 'donor3thirdparty', 'SoftCredit', 1),
	(900004, @fixture_fund, 'Anonymous', 'anonymous', 'Anonymous', 1),
	(900005, @fixture_fund, 'Donor5 Monthly', 'donor5monthly', 'AccountName', 1),
	(900006, @fixture_fund, 'Donor6 CatchUp', 'donor6catchup', 'AccountName', 1),
	(900007, @fixture_fund, 'Donor7 Quarterly', 'donor7quarterly', 'AccountName', 1),
	(900008, @fixture_fund, 'Donor8 Yearly', 'donor8yearly', 'AccountName', 1),
	(900009, @fixture_fund, 'Donor9 Sporadic', 'donor9sporadic', 'AccountName', 1),
	(900010, @fixture_fund, 'Donor10 Separate Subaccount', 'donor10separatesubaccount', 'AccountName', 1),
	(900011, @fixture_fund, 'Donor11 Merged Subaccount', 'donor11mergedsubaccount', 'AccountName', 1),
	(900012, @intern_fund, 'Donor12 Intern Gift', 'donor12interngift', 'AccountName', 1),
	(900013, @intern_fund, 'Donor13 Intern Gift', 'donor13interngift', 'AccountName', 1)
ON DUPLICATE KEY UPDATE
	DisplayName = VALUES(DisplayName),
	ResolutionSource = VALUES(ResolutionSource),
	ResolutionVersion = VALUES(ResolutionVersion),
	DateModified = UTC_TIMESTAMP();

INSERT INTO DonorContacts
	(DonorId, ContactType, ContactValue, NormalizedValue)
VALUES
	(900001, 'AddressLine1', 'Address1', 'address1'),
	(900001, 'City', 'City1', 'city1'),
	(900001, 'State', 'State1', 'state1'),
	(900001, 'PostalCode', '11111', '11111'),
	(900001, 'Country', 'Country1', 'country1'),
	(900001, 'HomePhone', '(111) 111-1111', '1111111111'),
	(900001, 'Email', 'donor1@example.test', 'donor1@example.test'),
	(900002, 'AddressLine1', 'Address2', 'address2'),
	(900002, 'MobilePhone', '(222) 222-2222', '2222222222')
ON DUPLICATE KEY UPDATE
	ContactValue = VALUES(ContactValue),
	DateModified = UTC_TIMESTAMP();

INSERT INTO DonationData
	(DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType, Amount,
	 Fund, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName,
	 IsAnonymous, Frequency)
VALUES
	-- Donor1: AccountName fallback and one-time gift.
	(900001, '2025-01-15 12:00:00', 'SYNTH-DONOR-001', 'Donor1', 'Check', 'Gift', 25.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 1),

	-- Donor2: PrimaryAddressee wins when SoftCreditName is contained in it.
	(900002, '2025-02-15 12:00:00', 'SYNTH-DONOR-002', 'Donor2 Account', 'Check', 'Gift', 50.00,
	 @fixture_fund, NULL, 'Donor2 Primary', 'Donor2', NULL, FALSE, 1),

	-- Donor3: unrelated third-party soft credit becomes the display identity.
	(900003, '2025-03-15 12:00:00', 'SYNTH-DONOR-003', 'Donor3 Account', 'Online', 'Gift', 75.00,
	 @fixture_fund, NULL, 'Donor3 Account', 'Donor3 ThirdParty', NULL, FALSE, 1),

	-- Donor4: anonymous gift suppresses identifying fields.
	(900004, '2025-04-15 12:00:00', 'SYNTH-DONOR-004', 'Hidden Account', 'Cash', 'Gift', 20.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, TRUE, 1),

	-- Donor5: monthly pattern with one missed month (within tolerance).
	(900005, '2025-01-15 12:00:00', 'SYNTH-DONOR-005-01', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900005, '2025-02-15 12:00:00', 'SYNTH-DONOR-005-02', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	-- March intentionally omitted.
	(900005, '2025-04-15 12:00:00', 'SYNTH-DONOR-005-04', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900005, '2025-05-15 12:00:00', 'SYNTH-DONOR-005-05', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900005, '2025-06-15 12:00:00', 'SYNTH-DONOR-005-06', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900005, '2025-07-15 12:00:00', 'SYNTH-DONOR-005-07', 'Donor5 Monthly', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),

	-- Donor6: April catch-up is $20, covering March and April at $10/month.
	(900006, '2025-01-15 12:00:00', 'SYNTH-DONOR-006-01', 'Donor6 CatchUp', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900006, '2025-02-15 12:00:00', 'SYNTH-DONOR-006-02', 'Donor6 CatchUp', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900006, '2025-04-15 12:00:00', 'SYNTH-DONOR-006-04-CATCHUP', 'Donor6 CatchUp', 'Online', 'Gift', 20.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900006, '2025-05-15 12:00:00', 'SYNTH-DONOR-006-05', 'Donor6 CatchUp', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),
	(900006, '2025-06-15 12:00:00', 'SYNTH-DONOR-006-06', 'Donor6 CatchUp', 'Online', 'Gift', 10.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 3),

	-- Donor7: approximately quarterly gifts.
	(900007, '2025-01-15 12:00:00', 'SYNTH-DONOR-007-01', 'Donor7 Quarterly', 'Check', 'Gift', 30.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 4),
	(900007, '2025-04-15 12:00:00', 'SYNTH-DONOR-007-04', 'Donor7 Quarterly', 'Check', 'Gift', 30.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 4),
	(900007, '2025-07-15 12:00:00', 'SYNTH-DONOR-007-07', 'Donor7 Quarterly', 'Check', 'Gift', 30.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 4),

	-- Donor8: approximately yearly gifts.
	(900008, '2024-01-15 12:00:00', 'SYNTH-DONOR-008-2024', 'Donor8 Yearly', 'Check', 'Gift', 100.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 5),
	(900008, '2025-01-15 12:00:00', 'SYNTH-DONOR-008-2025', 'Donor8 Yearly', 'Check', 'Gift', 100.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 5),

	-- Donor9: irregular spacing without catch-up/prepayment amounts.
	(900009, '2025-01-15 12:00:00', 'SYNTH-DONOR-009-01', 'Donor9 Sporadic', 'Cash', 'Gift', 100.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 2),
	(900009, '2025-03-15 12:00:00', 'SYNTH-DONOR-009-03', 'Donor9 Sporadic', 'Cash', 'Gift', 5.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 2),
	(900009, '2025-10-15 12:00:00', 'SYNTH-DONOR-009-10', 'Donor9 Sporadic', 'Cash', 'Gift', 5.00,
	 @fixture_fund, NULL, NULL, NULL, NULL, FALSE, 2)
ON DUPLICATE KEY UPDATE
	DonorId = VALUES(DonorId),
	AccountName = VALUES(AccountName),
	PaymentMethod = VALUES(PaymentMethod),
	GiftType = VALUES(GiftType),
	Amount = VALUES(Amount),
	PrimaryAddressee = VALUES(PrimaryAddressee),
	SoftCreditName = VALUES(SoftCreditName),
	IsAnonymous = VALUES(IsAnonymous),
	Frequency = VALUES(Frequency);
