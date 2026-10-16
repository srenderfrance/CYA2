# Production Cache Tuning TODO

These tasks must be completed after the production database and production workload are available. Do not guess the final warmup concurrency or query strategy from development measurements alone.

- [ ] Measure startup and database-update rebuild duration.
- [ ] Measure query count, query duration, rows returned, and payload size for each rebuild phase.
- [ ] Confirm production MySQL indexes for account, subaccount, donation date, `InternName`, accounting class, and account number lookups.
- [ ] Capture MySQL execution plans for the normal donation batch query, intern donation query, accounting batch query, and Admin Overview queries.
- [ ] Determine a safe warmup concurrency limit from MySQL CPU, connection-pool usage, memory, and concurrent user traffic.
- [ ] Test whether normal-account and intern donation data should be loaded in parallel or sequential phases.
- [ ] Verify that Admin Overview and financial summary rebuilds do not duplicate database reads.
- [ ] Measure cache hit rates after startup and after database updates.
- [ ] Test rebuild behavior while users are actively using Home and Admin.
- [ ] Verify that a database update arriving during a rebuild causes one follow-up rebuild for the latest generation.
- [ ] Review memory usage and LRU eviction under the production account and intern counts.
- [ ] Reassess whether additional bulk SQL or server-side aggregation is justified after collecting execution plans.
