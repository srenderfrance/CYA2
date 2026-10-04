# Synthetic donor edge cases

These expected results describe the fictional fixture in `database/fixtures/synthetic-donor-edge-cases.sql`. Use an isolated database after applying `database/schema/001_InitialSchema.sql`. The fixture is not loaded automatically.

## Scenarios

| Donor | Edge case | Expected result |
|---|---|---|
| Donor1 | Account-name fallback and current contacts | Display name is `Donor1`, because no primary addressee or soft credit name is present. The donor view should show `Address1`, `City1`, `State1`, `11111`, `Country1`, `(111) 111-1111`, and `donor1@example.test`. The single gift is `OneTime`. |
| Donor2 Primary | Primary addressee contains soft credit | Display name is `Donor2 Primary`. The soft credit value `Donor2` is contained in the primary addressee, so the primary addressee remains the identity. The gift is `OneTime`. |
| Donor3 ThirdParty | Third-party soft credit | Display name is `Donor3 ThirdParty`, because the soft credit name is not contained in `Donor3 Account`. The resolution source is `SoftCredit`. |
| Anonymous | Anonymous gift | Display name is `Anonymous`. The gift has no identifying addressee, soft credit, honor/memorial, or contact values. The anonymous gift remains included in donation totals but should not expose the source account identity. |
| Donor5 Monthly | One missed month | Gifts occur in January, February, April, May, June, and July. March is intentionally absent. The one-month gap is 1/6 of the observed span and is within the 25% missed-month tolerance, so the donor is `Monthly`. |
| Donor6 CatchUp | Catch-up payment | January and February are $10; April is $20 and represents the missed March amount plus April. The donor remains `Monthly`; the April gift should be eligible for catch-up classification and cover two months. |
| Donor7 Quarterly | Quarterly spacing | Gifts are three months apart in January, April, and July. The donor is `Quarterly`. |
| Donor8 Yearly | Yearly spacing | Gifts are twelve months apart in January 2024 and January 2025. The donor is `Yearly`. |
| Donor9 Sporadic | Irregular spacing | Gifts are two and seven month boundaries apart. The amounts are intentionally 100, 5, and 5 so the gaps are not interpreted as catch-up or prepayment coverage. The pattern is neither monthly, quarterly, nor yearly, so the donor is `Sporadic`. |

## Expected display and data checks

- Donor search and donor lists should use the `Donors.DisplayName` values above.
- Donation detail views should show the `PrimaryAddressee` and `SoftCreditName` values where present.
- Contact values are current-state projections in `DonorContacts`; they are not historical contact records.
- The anonymous donor must not display `Hidden Account` or any contact information.
- All fixture gift identifiers begin with `SYNTH-DONOR-`, making them easy to identify in an isolated test database.

## Verification queries

```sql
SELECT DonorId, COUNT(*) AS GiftCount, SUM(Amount) AS TotalAmount,
	   MIN(Date) AS FirstGift, MAX(Date) AS LastGift
FROM DonationData
WHERE GiftImportId LIKE 'SYNTH-DONOR-%'
GROUP BY DonorId
ORDER BY DonorId;

SELECT Id, DisplayName, IdentityKey, ResolutionSource
FROM Donors
WHERE Id BETWEEN 900001 AND 900009
ORDER BY Id;

SELECT DonorId, ContactType, ContactValue
FROM DonorContacts
WHERE DonorId BETWEEN 900001 AND 900009
ORDER BY DonorId, ContactType;
```

## Limitations

The script resets only its own fixture rows before inserting canonical projection rows. It does not exercise workbook parsing, import validation, donor identity resolution, or automatic frequency recalculation. Use the application's synthetic `.xlsx` import fixtures for those end-to-end behaviors when they are created. The persisted `Frequency` values are set to the expected enum values for stable display testing.
