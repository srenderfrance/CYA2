CREATE TABLE IF NOT EXISTS DonationDataBackup
(
	BackupId CHAR(36) NOT NULL,
	Id BIGINT NOT NULL,
	DonorId BIGINT NOT NULL,
	Date DATETIME NOT NULL,
	GiftImportId VARCHAR(255) NULL,
	AccountName VARCHAR(255) NOT NULL,
	PaymentMethod VARCHAR(255) NOT NULL,
	GiftType VARCHAR(255) NOT NULL,
	Amount DECIMAL(18,2) NOT NULL,
	Fund VARCHAR(255) NOT NULL,
	InternName VARCHAR(255) NULL,
	PrimaryAddressee VARCHAR(255) NULL,
	SoftCreditName VARCHAR(255) NULL,
	IsAnonymous BOOLEAN NOT NULL DEFAULT FALSE,
	Frequency TINYINT NULL,
	DateCreated DATETIME NOT NULL,
	BackupAt DATETIME NOT NULL,
	Pinned BOOLEAN NOT NULL DEFAULT FALSE,
	SourceRangeStart DATETIME NULL,
	PRIMARY KEY (BackupId, Id),
	KEY ix_donation_data_backup_backup_at (BackupAt),
	KEY ix_donation_data_backup_pinned (Pinned),
	KEY ix_donation_data_backup_backup_id (BackupId)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS AccountingDataBackup
(
	BackupId CHAR(36) NOT NULL,
	Id BIGINT NOT NULL,
	AccountingClass VARCHAR(255) NULL,
	Date DATETIME NULL,
	Num VARCHAR(255) NULL,
	Amount DECIMAL(18,2) NULL,
	AccountNumber VARCHAR(255) NULL,
	Account VARCHAR(255) NULL,
	Type VARCHAR(255) NULL,
	DateCreated DATETIME NULL,
	BackupAt DATETIME NOT NULL,
	Pinned BOOLEAN NOT NULL DEFAULT FALSE,
	SourceRangeStart DATETIME NULL,
	PRIMARY KEY (BackupId, Id),
	KEY ix_accounting_data_backup_backup_at (BackupAt),
	KEY ix_accounting_data_backup_pinned (Pinned),
	KEY ix_accounting_data_backup_backup_id (BackupId)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;
