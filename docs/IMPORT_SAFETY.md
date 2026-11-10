# Financial import safety

Donation and accounting uploads are replacement operations, not append-only imports. The replacement window starts at the earliest accepted incoming date and extends forward. Earlier donations/accounting rows remain. Use complete exports for the intended replacement window.

## Publication contract

1. Parse the entire workbook and reject the whole upload if any populated row is invalid. Empty workbooks do not replace data. Known donation duplicates are consolidated before persistence.
   A row is skipped only when every cell is blank. Prior donation frequency history and incoming gifts use the same donor identity plus fund, not source/display names.
2. Acquire the shared financial-write gate. Donation/accounting processors, rollback and recategorization share the in-process gate. A connection-owned MySQL advisory lock also serializes persistent publication, rollback and recategorization across sessions. This is not a distributed cache-coherence or multi-worker guarantee.
3. Populate connection-local temporary staging tables before starting the persistent transaction. Runtime credentials need temporary-table rights, not persistent schema-management rights. No staging TRUNCATE, local-infile loader, live row fallback or automatic publication retry is used.
4. In one InnoDB transaction, create backup metadata/payload, delete the overlapping live range, publish staged rows and reconcile current donor contacts. Verify published counts before commit. Failures before commit roll back these persistent changes together.
5. Invalidate donation and donor-contact generations after a committed donation replacement; invalidate accounting after a committed accounting replacement. Uncertain outcomes conservatively invalidate the affected domains too. Existing summary/overview rebuild scheduling remains unchanged.

The database default must be `utf8mb4` / `utf8mb4_0900_ai_ci`: temporary donation staging depends on the database default, while some expressions use explicit `utf8mb4` collations. An incompatible default can cause MySQL error 1253. A database default change does not convert existing tables or prove that a failed import made no changes; reconcile the outcome before retrying. See [database setup](../database/README.md#initial-setup) and [Azure troubleshooting](AZURE_SETUP.md#12-troubleshooting).

Donation snapshots cover all donations and donor/contact projections because global identity resolution can relink retained gifts outside the replacement window. Rollback restores that complete state. Accounting snapshots cover all existing accounting rows. Both retain the latest unpinned snapshot and all pinned snapshots, with matching payload. Pinned snapshots currently require operational retention review; they are not automatically expired.

Contact reconciliation is current-state only: direct gifts in the upload replace the resolved donor's contact set; soft-credit-only uploads preserve verified direct contacts. New contact values receive DateCreated with DateModified null. Retained values preserve DateCreated; DateModified is set only when the exact stored ContactValue changes, including casing-only display changes with the same normalized identity. Unchanged values preserve DateModified, including null. Removed values are deleted, and an empty direct contact set clears that donor's contacts. Identity promotion preserves transferred contact timestamps; ambiguous soft-credit-only name identities do not receive constituent-specific contacts. Rollback restores snapshot values and both timestamps.

The evolving `database/schema/001_InitialSchema.sql` defines snapshot timestamp microsecond precision and the shared donor schema. Publication assigns a strictly increasing timestamp under the write lock. Existing development databases require explicit recreation using the updated initial schema and a fresh upload; the application does not automatically convert an existing per-fund schema or record a new migration for this change.

## Constituent identity

Donation workbooks must contain `Constituent ID`; blank cells are accepted. IDs are opaque text (maximum 255 characters), preserving leading zeros. `DonationData.ConstituentId` retains the source ID; `Donors.ConstituentId` identifies the resolved credited donor. DonorContacts references that shared donor through DonorId rather than duplicating constituent IDs.

Direct non-anonymous gifts use their ID. Soft-credit identities never use the intermediary's ID or contacts. Name-only gifts match an exact normalized name only when existing direct gifts and the entire upload establish one constituent ID; multiple IDs keep the name-only donor separate. Existing gift evidence is considered even when its range is replaced. Anonymous gifts do not contribute identity evidence. Name-only matches cannot distinguish unrelated people with identical normalized names.

Donors are global display projections across funds, while each gift retains its fund and intern scope. A direct upload replaces the donor's current contact set, including clearing missing values. A soft-credit-only upload preserves existing direct contacts. New direct evidence can relink retained name-only gifts; later ambiguous evidence separates those gifts again. These changes and their backups publish atomically. No third-party contact values are imported for credited donors.

## Cancellation and uncertain commits

Cancellation is honored while waiting, parsing, staging and preparing the transaction. Once commit starts, caller cancellation does not interrupt acknowledgement. If acknowledgement fails, a separate connection checks the unique snapshot marker. A visible marker confirms publication; otherwise the app reports that the outcome could not be confirmed, invalidates affected caches and requires reconciliation. It never automatically retries publication.

For an uncertain result, do not immediately re-upload. Check the latest snapshot and application-visible amounts/contact projections, confirm database connectivity and reconcile whether publication occurred. Rollback only after confirming the intended snapshot. Cache rebuilding and progress/audit delivery are separate post-commit work, not part of the database transaction. An application/process failure can interrupt that work; verify displayed data after reconnecting. The in-memory work queue is not durable across restarts.

Committed imports carry `PublicationCommitted` and separate `Warnings`. Cache/progress exceptions produce warnings rather than changing the publication outcome; terminal audit delivery cannot relabel a committed upload as failed. Cache-refresh warnings require a reload and verification, not an automatic re-upload. If progress delivery itself fails, the progress display may remain incomplete even though publication committed.

Rollback uses non-cancellable commit acknowledgement and verifies the selected snapshot's transactionally committed `Pinned` marker after an acknowledgement failure. An inconclusive result sets `OutcomeUnknown` and requires reconciliation before retrying. Combined rollback stops after an unknown donation outcome; otherwise it preserves `ChangesCommitted` even when accounting fails or is cancelled. Successful, partial and uncertain changes all request cache invalidation. Admin displays partial-commit notices and notification warnings. Combined rollback is still two transactions, not an atomic two-domain operation.

## Validation and remaining acceptance

The ordinary test suite exercises actual workbook processors with synthetic in-memory workbooks and fake persistence: invalid/empty rejection, valid publication, cancellation, affected-domain invalidation and cross-processor serialization. Opt-in Docker tests exercise initial schema setup, restricted runtime permissions, SQL failure rollback against existing data, prior-history donor contact recovery, accounting rollback and cancelled replacement. See [database tests](DATABASE_TESTS.md).

Enabled disposable MySQL runs cover contact timestamp preservation, identity promotion, ambiguity isolation and full snapshot rollback. Additional private-environment acceptance includes forced disconnect during commit, concurrent imports/rollback from separate app processes, large real-format exports, warm-cache UI contact checks and shutdown/reconnect behavior. This change does not resolve general exception privacy, durable queueing or other [production review](PRODUCTION_REVIEW.md) items.
