-- CYA2 initial application database schema
--
-- This script creates the clean schema used by the modern CYA2 application.
-- It does not migrate the legacy PHP database and does not load donor or
-- accounting data. Populate application data through the normal source-system
-- Excel import workflows.
--
-- Target: MySQL 8.0+ (the application uses utf8mb4_0900_ai_ci explicitly).
-- Run this script after selecting the target database. Do not add credentials
-- or CREATE USER statements to this file.

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS Accounts
(
	AccountId INT NOT NULL AUTO_INCREMENT,
	Fund VARCHAR(255) NOT NULL,
	AccountingClass VARCHAR(255) NOT NULL,
	AccountNumber VARCHAR(255) NOT NULL,
	CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	Overhead DECIMAL(10,2) NOT NULL DEFAULT 0.00,
	SoftCredit VARCHAR(255) NOT NULL DEFAULT '',
	BalanceAdjustment DECIMAL(18,2) NOT NULL DEFAULT 0.00,
	OtherFunds BOOLEAN NOT NULL DEFAULT FALSE,
	PRIMARY KEY (AccountId),
	UNIQUE KEY uq_accounts_fund (Fund),
	UNIQUE KEY uq_accounts_account_number (AccountNumber),
	KEY ix_accounts_accounting_class (AccountingClass)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS SubAccounts
(
	Id INT NOT NULL AUTO_INCREMENT,
	AccountId INT NOT NULL,
	SubFund VARCHAR(255) NOT NULL,
	Kind VARCHAR(32) NOT NULL,
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	DateModified DATETIME NULL,
	PRIMARY KEY (Id),
	UNIQUE KEY uq_subaccounts_account_fund_kind (AccountId, SubFund, Kind),
	KEY ix_subaccounts_account_id (AccountId),
	CONSTRAINT fk_subaccounts_account
		FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId)
		ON DELETE CASCADE
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS Users
(
	Id INT NOT NULL AUTO_INCREMENT,
	GoogleId VARCHAR(255) NOT NULL,
	Email VARCHAR(320) NOT NULL,
	Name VARCHAR(255) NOT NULL,
	Language VARCHAR(16) NOT NULL DEFAULT 'en',
	AuthLevel VARCHAR(32) NOT NULL DEFAULT 'User',
	DefaultAccount INT NULL,
	Prefrence VARCHAR(64) NOT NULL DEFAULT 'default',
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	DateModified DATETIME NULL,
	PRIMARY KEY (Id),
	UNIQUE KEY uq_users_google_id (GoogleId),
	UNIQUE KEY uq_users_email (Email),
	KEY ix_users_default_account (DefaultAccount),
	CONSTRAINT fk_users_default_account
		FOREIGN KEY (DefaultAccount) REFERENCES Accounts (AccountId)
		ON DELETE SET NULL
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS AccountsUsers
(
	UserId INT NOT NULL,
	AccountId INT NOT NULL,
	PRIMARY KEY (UserId, AccountId),
	KEY ix_accounts_users_account_id (AccountId),
	CONSTRAINT fk_accounts_users_user
		FOREIGN KEY (UserId) REFERENCES Users (Id)
		ON DELETE CASCADE
		ON UPDATE CASCADE,
	CONSTRAINT fk_accounts_users_account
		FOREIGN KEY (AccountId) REFERENCES Accounts (AccountId)
		ON DELETE CASCADE
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS Donors
(
	Id BIGINT NOT NULL AUTO_INCREMENT,
	Fund VARCHAR(255) NOT NULL,
	DisplayName VARCHAR(255) NOT NULL,
	IdentityKey VARCHAR(512) NOT NULL,
	ResolutionSource VARCHAR(32) NOT NULL,
	ResolutionVersion INT NOT NULL,
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	DateModified DATETIME NULL,
	PRIMARY KEY (Id),
	UNIQUE KEY uq_donors_fund_identity (Fund, IdentityKey),
	KEY ix_donors_fund (Fund),
	KEY ix_donors_display_name (DisplayName)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS DonorContacts
(
	Id BIGINT NOT NULL AUTO_INCREMENT,
	DonorId BIGINT NOT NULL,
	ContactType VARCHAR(32) NOT NULL,
	ContactValue VARCHAR(500) NOT NULL,
	NormalizedValue VARCHAR(500) NOT NULL,
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	DateModified DATETIME NULL,
	PRIMARY KEY (Id),
	UNIQUE KEY uq_donor_contacts_value (DonorId, ContactType, NormalizedValue),
	KEY ix_donor_contacts_donor_id (DonorId),
	KEY ix_donor_contacts_type_value (ContactType, NormalizedValue(200)),
	CONSTRAINT fk_donor_contacts_donor
		FOREIGN KEY (DonorId) REFERENCES Donors (Id)
		ON DELETE CASCADE
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS DonationData
(
	Id BIGINT NOT NULL AUTO_INCREMENT,
	DonorId BIGINT NOT NULL,
	Date DATETIME NOT NULL,
	GiftImportId VARCHAR(255) NULL,
	AccountName VARCHAR(255) NOT NULL,
	PaymentMethod VARCHAR(255) NOT NULL,
	GiftType VARCHAR(255) NOT NULL,
	Amount DECIMAL(18,2) NOT NULL,
	Fund VARCHAR(255) NOT NULL,
	Intern VARCHAR(255) NULL,
	PrimaryAddressee VARCHAR(255) NULL,
	SoftCreditName VARCHAR(255) NULL,
	HonorMemorialName VARCHAR(255) NULL,
	IsAnonymous BOOLEAN NOT NULL DEFAULT FALSE,
	Frequency TINYINT NULL,
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY (Id),
	KEY ix_donation_data_donor_id (DonorId),
	KEY ix_donation_data_date (Date),
	KEY ix_donation_data_fund_date (Fund, Date),
	KEY ix_donation_data_gift_import_id (GiftImportId),
	CONSTRAINT fk_donation_data_donor
		FOREIGN KEY (DonorId) REFERENCES Donors (Id)
		ON DELETE RESTRICT
		ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS AccountingData
(
	Id BIGINT NOT NULL AUTO_INCREMENT,
	AccountingClass VARCHAR(255) NOT NULL,
	Date DATETIME NOT NULL,
	Num VARCHAR(255) NOT NULL,
	Amount DECIMAL(18,2) NOT NULL,
	AccountNumber VARCHAR(255) NOT NULL,
	Account VARCHAR(255) NOT NULL,
	Type VARCHAR(255) NOT NULL,
	DateCreated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY (Id),
	KEY ix_accounting_data_date (Date),
	KEY ix_accounting_data_class_date (AccountingClass, Date),
	KEY ix_accounting_data_account_number_date (AccountNumber, Date)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

-- Donation rollback payload and metadata. These tables are schema objects,
-- not a copy of the legacy database. The application fills and cleans them
-- during an import rollback window.
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
	Intern VARCHAR(255) NULL,
	PrimaryAddressee VARCHAR(255) NULL,
	SoftCreditName VARCHAR(255) NULL,
	HonorMemorialName VARCHAR(255) NULL,
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

CREATE TABLE IF NOT EXISTS DonorsBackup
(
	BackupId CHAR(36) NOT NULL,
	Id BIGINT NOT NULL,
	Fund VARCHAR(255) NOT NULL,
	DisplayName VARCHAR(255) NOT NULL,
	IdentityKey VARCHAR(512) NOT NULL,
	ResolutionSource VARCHAR(32) NOT NULL,
	ResolutionVersion INT NOT NULL,
	DateCreated DATETIME NOT NULL,
	DateModified DATETIME NULL,
	BackupAt DATETIME NOT NULL,
	PRIMARY KEY (BackupId, Id),
	KEY ix_donors_backup_backup_id (BackupId)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS DonorContactsBackup
(
	BackupId CHAR(36) NOT NULL,
	Id BIGINT NOT NULL,
	DonorId BIGINT NOT NULL,
	ContactType VARCHAR(32) NOT NULL,
	ContactValue VARCHAR(500) NOT NULL,
	NormalizedValue VARCHAR(500) NOT NULL,
	DateCreated DATETIME NOT NULL,
	DateModified DATETIME NULL,
	BackupAt DATETIME NOT NULL,
	PRIMARY KEY (BackupId, Id),
	KEY ix_donor_contacts_backup_backup_id (BackupId),
	KEY ix_donor_contacts_backup_donor_id (BackupId, DonorId)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS DonationBackupSnapshots
(
	BackupId CHAR(36) NOT NULL PRIMARY KEY,
	BackupAt DATETIME NOT NULL,
	SourceRangeStart DATETIME NOT NULL,
	RecordCount INT NOT NULL DEFAULT 0,
	Pinned BOOLEAN NOT NULL DEFAULT FALSE,
	KEY ix_donation_backup_snapshots_backup_at (BackupAt),
	KEY ix_donation_backup_snapshots_pinned (Pinned)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;

-- Accounting rollback payload and metadata.
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

CREATE TABLE IF NOT EXISTS AccountingBackupSnapshots
(
	BackupId CHAR(36) NOT NULL PRIMARY KEY,
	BackupAt DATETIME NOT NULL,
	SourceRangeStart DATETIME NOT NULL,
	RecordCount INT NOT NULL DEFAULT 0,
	Pinned BOOLEAN NOT NULL DEFAULT FALSE,
	KEY ix_accounting_backup_snapshots_backup_at (BackupAt),
	KEY ix_accounting_backup_snapshots_pinned (Pinned)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_0900_ai_ci;
