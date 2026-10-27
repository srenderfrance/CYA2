-- Restricted audit stream for privileged operations.
-- Stores only internal numeric actor IDs and non-sensitive operation metadata.

CREATE TABLE AuditEvents
(
	Id BIGINT NOT NULL AUTO_INCREMENT,
	ActorUserId INT NULL,
	EventType VARCHAR(64) NOT NULL,
	Outcome VARCHAR(32) NOT NULL,
	OccurredAtUtc DATETIME NOT NULL,
	TraceId VARCHAR(64) NULL,
	ItemCount INT NULL,
	PRIMARY KEY (Id),
	KEY ix_audit_events_occurred_at_utc (OccurredAtUtc),
	KEY ix_audit_events_actor_occurred_at_utc (ActorUserId, OccurredAtUtc),
	CONSTRAINT fk_audit_events_actor
		FOREIGN KEY (ActorUserId) REFERENCES Users (Id)
		ON DELETE SET NULL
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;
