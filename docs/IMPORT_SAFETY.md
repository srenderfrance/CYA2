# Financial import safety

Donation and accounting uploads are replacement operations, not append-only imports. The replacement window starts at the earliest accepted incoming date and extends forward. Earlier donations/accounting rows remain. Use complete exports for the intended replacement window.

## Publication contract

1. Parse the entire workbook and reject the whole upload if any populated row is invalid. Empty workbooks do not replace data. Known donation duplicates are consolidated before persistence.
   A row is skipped only when every cell is blank. Prior donation frequency history and incoming gifts use the same donor identity plus fund, not source/display names.
2. Acquire the shared financial-write gate. Donation/accounting processors, rollback and recategorization share the in-process gate. A connection-owned MySQL advisory lock also serializes persistent publication, rollback and recategorization across sessions. This is not a distributed cache-coherence or multi-worker guarantee.
3. Populate connection-local temporary staging tables before starting the persistent transaction. Runtime credentials need temporary-table rights, not persistent schema-management rights. No staging TRUNCATE, local-infile loader, live row fallback or automatic publication retry is used.
4. In one InnoDB transaction, create backup metadata/payload, delete the overlapping live range, publish staged rows and reconcile current donor contacts. Verify published counts before commit. Failures before commit roll back these persistent changes together.
5. Invalidate donation and donor-contact generations after a committed donation replacement; invalidate accounting after a committed accounting replacement. Uncertain outcomes conservatively invalidate the affected domains too. Existing summary/overview rebuild scheduling remains unchanged.

Donation snapshots cover replaced donations and every existing donor/contact projection touched by incoming identities, including donors whose prior gifts are earlier than the replacement window. Accounting snapshots cover all existing accounting rows. Both retain the latest unpinned snapshot and all pinned snapshots, with matching payload. Pinned snapshots currently require operational retention review; they are not automatically expired.

Contact reconciliation is current-state only and limited to donors included in the upload. New contact values receive DateCreated with DateModified null. Retained values preserve DateCreated; DateModified is set only when the exact stored ContactValue changes, including casing-only display changes with the same normalized identity. Unchanged values preserve DateModified, including null. Removed values are deleted, and an empty contact set clears only the imported donor's contacts. Rollback restores snapshot values and both timestamps. No schema changes or migration-history recording are required for this fix.

Migration `003_SnapshotTimestampPrecision.sql` gives snapshot timestamps microsecond precision. Publication assigns a strictly increasing timestamp under the write lock so rapid imports select the correct latest rollback snapshot. Run the normal migrator before using the updated application; do not modify an already-applied baseline SQL file.

## Cancellation and uncertain commits

Cancellation is honored while waiting, parsing, staging and preparing the transaction. Once commit starts, caller cancellation does not interrupt acknowledgement. If acknowledgement fails, a separate connection checks the unique snapshot marker. A visible marker confirms publication; otherwise the app reports that the outcome could not be confirmed, invalidates affected caches and requires reconciliation. It never automatically retries publication.

For an uncertain result, do not immediately re-upload. Check the latest snapshot and application-visible amounts/contact projections, confirm database connectivity and reconcile whether publication occurred. Rollback only after confirming the intended snapshot. Cache rebuilding and progress/audit delivery are separate post-commit work, not part of the database transaction. An application/process failure can interrupt that work; verify displayed data after reconnecting. The in-memory work queue is not durable across restarts.

Committed imports carry `PublicationCommitted` and separate `Warnings`. Cache/progress exceptions produce warnings rather than changing the publication outcome; terminal audit delivery cannot relabel a committed upload as failed. Cache-refresh warnings require a reload and verification, not an automatic re-upload. If progress delivery itself fails, the progress display may remain incomplete even though publication committed.

Rollback uses non-cancellable commit acknowledgement and verifies the selected snapshot's transactionally committed `Pinned` marker after an acknowledgement failure. An inconclusive result sets `OutcomeUnknown` and requires reconciliation before retrying. Combined rollback stops after an unknown donation outcome; otherwise it preserves `ChangesCommitted` even when accounting fails or is cancelled. Successful, partial and uncertain changes all request cache invalidation. Admin displays partial-commit notices and notification warnings. Combined rollback is still two transactions, not an atomic two-domain operation.

## Validation and remaining acceptance

The ordinary test suite exercises actual workbook processors with synthetic in-memory workbooks and fake persistence: invalid/empty rejection, valid publication, cancellation, affected-domain invalidation and cross-processor serialization. Opt-in Docker tests exercise real migrations, restricted runtime permissions, SQL failure rollback against existing data, prior-history donor contact recovery, accounting rollback and cancelled replacement. See [database tests](DATABASE_TESTS.md).

Docker SQL scenarios still require an enabled run before MySQL transaction behavior is accepted, including the contact timestamp and rollback regressions. Additional private-environment acceptance includes forced disconnect during commit, concurrent imports/rollback from separate app processes, large real-format exports, warm-cache UI contact checks and shutdown/reconnect behavior. This change does not resolve general exception privacy, durable queueing or other [production review](PRODUCTION_REVIEW.md) items.
