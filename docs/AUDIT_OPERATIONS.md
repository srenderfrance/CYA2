# Restricted audit operations

## Evidence and failure policy

`AuditEvents` contains fixed event/outcome labels, UTC timestamps, generated operation and host-instance identifiers, optional HTTP trace identifiers, numeric actor/target user IDs and counts. It must not contain donor/contact values, intern names, fund/subfund values, workbook contents or credentials. Actor and target IDs deliberately have no user foreign key: deletion must not erase historical numeric evidence. Restrict database access accordingly.

Supported user administration (including initial Admin bootstrap), donation/accounting publication and each rollback phase insert required success evidence **inside the data transaction**. Failure to insert prevents that transaction from committing. A saved success event is publication evidence even if subsequent progress/cache delivery fails. It is not proof the browser displayed the result.

Imports require a saved `Started` event before processing or enqueueing. The server-generated progress ID is the stable operation ID through the queue and publication. Required-start failure consumes the preview but does not publish; upload a new preview after fixing the audit store. The queue is still memory-only, not a durable job system.

Exports save required `DonorExportCompleted` evidence after workbook generation and before returning file bytes. Audit failure withholds the export. This means **server generation**, not confirmed client receipt. Failure and denied-operation events are best-effort and independently signal persistence failures through monitoring logs. A missing event cannot prove that no attempt occurred during a database outage.

`Succeeded`, `Failed`, `Cancelled`, `Unknown`, `Partial`, and `NeedsReconciliation` are distinct. Unknown acknowledgement is not proof of failure. Combined rollback has separate donation/accounting transactional success events; its summary can be Partial or Unknown. Confirmed committed work must not be automatically retried after notification failures. Request-validation failures before an operation starts are not a complete security request log.

## Monitoring: logs only

The selected delivery mechanism is **monitoring logs only for now**. No email, webhook, external alert rule or monitored sink is configured by this change. Ensure the deployed provider retains Warning/Error/Critical events and establish an operator review schedule; logs alone do not guarantee anyone is notified.

| Event ID / name | Meaning | Response |
| --- | --- | --- |
| 9100 / AuditWriteFailed (Critical) | Required or best-effort evidence could not be persisted | Check database availability, audit schema, runtime grants and storage. Use the generated OperationId to correlate; do not copy private database error text into general logs. |
| 9101 / AuditReconciliationUnavailable (Error) | Restart scan failed; retries every minute | Restore connectivity/schema compatibility and confirm a later successful scan. |
| 9102 / AuditReconciliationRequired (Warning) | Prior-host unfinished imports were flagged; count only | Inspect restricted NeedsReconciliation records and reconcile each operation. |
| 9103 / AuditReviewRequired (Warning) | An Unknown, Partial or NeedsReconciliation outcome requires review | Inspect phase success events, snapshots and UI before another mutation. |

Tests exercise required persistence failure and sanitized 9100 output. Production provider delivery, retention and operator response still require deployment acceptance. Existing cleanup warnings also require review.

## Restart and uncertain-outcome procedure

The hosted scan runs at startup and every minute. It flags prior-process starts older than five minutes with no recorded terminal outcome. The marker is idempotent and preserves the start. Current-host and recent work is excluded. No replay or automatic rollback occurs. This policy assumes the supported **single active app process**; parallel live instances need shared durable job ownership/leases before using this inference.

1. Pause further uploads/rollback for the affected data. Restore connectivity first.
2. In the restricted database, correlate records by OperationId. Inspect domain completion events and counts; do not infer failure merely from missing progress. A committed transactional completion is stronger evidence than an uncertain acknowledgement.
3. Compare snapshot metadata and application-visible totals/contact projections to the intended upload or rollback. Combined rollback must be checked separately for both domains.
4. If publication remains uncertain, retain the marker and investigate; do not blindly re-upload. Choose a verified snapshot for any rollback and verify UI results after cache refresh.
5. Document the operator's reconciliation decision in a restricted incident record. There is no reconciliation admin screen or automatic marker-resolution action in this change.

If a separate failure record also could not be saved, a stale start may later be flagged even though the operation stopped before publication. NeedsReconciliation means evidence is incomplete, not that data changed. Current-host stranded work is not automatically classified by this restart scan.

## Schema, retention and rollout

All pre-production schema changes are in `database/schema/001_InitialSchema.sql`; no incremental migration or schema-history entry is added. Existing databases are **not automatically aligned**. The audit table now requires TargetUserId, OperationId, HostInstanceId and the operation/event/outcome unique key, and removes the former actor foreign key. Publishing the binary against an older audit table will prevent required writes/exports and trigger monitoring failures.

Before deploying, compare the actual schema, preserve any required data/backup history, and explicitly approve an alignment or disposable-database recreation procedure. The fresh initializer rejects nonempty databases. Do not reset a populated database merely to satisfy the new schema. This implementation did not modify any existing database.

Application cleanup removes events older than 90 days on startup and daily. Failed cleanup retries at the next daily run. Backups and independent log sinks have their own retention/access policies; row deletion does not erase older backups. Confirm those policies before using real personal data.

## Validation boundary

Disposable MySQL tests cover required audit failure rolling back bootstrap, user creation/update/deletion, both imports and both rollback phases; operation correlation; historical numeric references; deduplication; restart exclusions; and the retention boundary. Application/Web tests cover safe monitoring, required starts, export release and truthful outcomes. Actual process kill/lost commit acknowledgement, real browser downloads, deployed MySQL/provider behavior and monitored incident response remain separate acceptance work. Release automation is unchanged.
