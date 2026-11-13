# Production Cache Tuning TODO

Current priorities are in [Production review](PRODUCTION_REVIEW.md): establish the safe pre-live baseline in B11, then use C01 for ongoing workload-driven tuning. This detailed measurement checklist is retained as supporting acceptance criteria, not a separate competing backlog. Correctness defects A01–A07 must not be deferred as performance tuning.

Intern-specific query/index and rebuild-phase tuning is lowest-priority C09, not a launch gate: the organization has few interns, most serve about one year and almost none exceed two years, and related donation volume is insignificant. Revisit only if workload or measurements justify it. Intern identity and isolation correctness remain separate acceptance requirements.

These tasks must be completed after the production database and production workload are available. Do not guess the final warmup concurrency or query strategy from development measurements alone.

- [ ] Measure startup and database-update rebuild duration.
- [ ] Measure query count, query duration, rows returned, and payload size for each rebuild phase.
- [ ] Confirm production MySQL indexes for account, subaccount, donation date, accounting class, and account number lookups.
- [ ] Capture MySQL execution plans for the normal donation batch query, accounting batch query, and Admin Overview queries.
- [ ] Determine a safe warmup concurrency limit from MySQL CPU, connection-pool usage, memory, and concurrent user traffic.
- [ ] Verify that Admin Overview and financial summary rebuilds do not duplicate database reads.
- [ ] Measure cache hit rates after startup and after database updates.
- [ ] Test rebuild behavior while users are actively using Home and Admin.
- [ ] Verify that a database update arriving during a rebuild causes one follow-up rebuild for the latest generation.
- [ ] Review memory usage and LRU eviction under the production account and intern counts.
- [ ] Reassess whether additional bulk SQL or server-side aggregation is justified after collecting execution plans.

## Lowest priority: intern-specific performance (C09)

- [ ] Inspect `InternName` lookup indexes and execution plans if workload or measurements justify it.
- [ ] Evaluate parallel versus sequential intern donation loading if workload or measurements justify it.
