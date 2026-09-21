-- CYA2 donation schema rebuild
-- Destructive: this script removes the existing donation and donor/contact tables.
-- Run only after confirming the database has been backed up or is disposable.
--
-- Design:
--   Donors is scoped by Fund; it is not a global donor identity table.
--   DonationData contains one canonical financial row per fund allocation.
--   GiftImportId identifies the source gift, but is not unique by itself because
--   one gift may be allocated to multiple funds.
--   DonorContacts is the current display projection for email, phone, and
--   address values. Each upload reconciles it; obsolete values are removed.
--   Duplicate detection remains application business logic because this export
--   does not provide an allocation-level identifier.



DROP TABLE IF EXISTS `DonorContacts`;
DROP TABLE IF EXISTS `DonorContactsBackup`;
DROP TABLE IF EXISTS `DonorsBackup`;
DROP TABLE IF EXISTS `DonationDataBackup`;
DROP TABLE IF EXISTS `DonationBackupSnapshots`;
DROP TABLE IF EXISTS `AccountingBackupSnapshots`;
DROP TABLE IF EXISTS `DonationData`;
DROP TABLE IF EXISTS `Donors`;

SET FOREIGN_KEY_CHECKS = 1;

CREATE TABLE `Donors`
(
	`Id` BIGINT NOT NULL AUTO_INCREMENT,
	`Fund` VARCHAR(255) NOT NULL,
	`DisplayName` VARCHAR(255) NOT NULL,
	`IdentityKey` VARCHAR(512) NOT NULL,
	`ResolutionSource` VARCHAR(32) NOT NULL,
	`ResolutionVersion` INT NOT NULL DEFAULT 1,
	`DateCreated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	`DateModified` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
	PRIMARY KEY (`Id`),
	UNIQUE KEY `ux_Donors_Fund_IdentityKey` (`Fund`, `IdentityKey`),
	KEY `ix_Donors_Fund_DisplayName` (`Fund`, `DisplayName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `AccountingBackupSnapshots`
(
	`BackupId` CHAR(36) NOT NULL,
	`BackupAt` DATETIME NOT NULL,
	`SourceRangeStart` DATETIME NOT NULL,
	`RecordCount` INT NOT NULL DEFAULT 0,
	`Pinned` BOOLEAN NOT NULL DEFAULT FALSE,
	PRIMARY KEY (`BackupId`),
	KEY `ix_AccountingBackupSnapshots_BackupAt` (`BackupAt`),
	KEY `ix_AccountingBackupSnapshots_Pinned` (`Pinned`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- One row per donation upload, including uploads where no prior rows existed
-- in the replaced date range. Payload rows remain in DonationDataBackup.
CREATE TABLE `DonationBackupSnapshots`
(
	`BackupId` CHAR(36) NOT NULL,
	`BackupAt` DATETIME NOT NULL,
	`SourceRangeStart` DATETIME NOT NULL,
	`RecordCount` INT NOT NULL DEFAULT 0,
	`Pinned` BOOLEAN NOT NULL DEFAULT FALSE,
	PRIMARY KEY (`BackupId`),
	KEY `ix_DonationBackupSnapshots_BackupAt` (`BackupAt`),
	KEY `ix_DonationBackupSnapshots_Pinned` (`Pinned`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `DonorsBackup`
(
	`BackupId` CHAR(36) NOT NULL,
	`Id` BIGINT NOT NULL,
	`Fund` VARCHAR(255) NOT NULL,
	`DisplayName` VARCHAR(255) NOT NULL,
	`IdentityKey` VARCHAR(512) NOT NULL,
	`ResolutionSource` VARCHAR(32) NOT NULL,
	`ResolutionVersion` INT NOT NULL,
	`DateCreated` DATETIME NOT NULL,
	`DateModified` DATETIME NOT NULL,
	`BackupAt` DATETIME NOT NULL,
	PRIMARY KEY (`BackupId`, `Id`),
	KEY `ix_DonorsBackup_BackupId` (`BackupId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `DonorContactsBackup`
(
	`BackupId` CHAR(36) NOT NULL,
	`Id` BIGINT NOT NULL,
	`DonorId` BIGINT NOT NULL,
	`ContactType` VARCHAR(32) NOT NULL,
	`ContactValue` VARCHAR(500) NOT NULL,
	`NormalizedValue` VARCHAR(500) NOT NULL,
	`DateCreated` DATETIME NOT NULL,
	`DateModified` DATETIME NOT NULL,
	`BackupAt` DATETIME NOT NULL,
	PRIMARY KEY (`BackupId`, `Id`),
	KEY `ix_DonorContactsBackup_BackupId` (`BackupId`),
	KEY `ix_DonorContactsBackup_DonorId` (`BackupId`, `DonorId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `DonationData`
(
	`Id` INT NOT NULL AUTO_INCREMENT,
	`DonorId` BIGINT NOT NULL,
	`Date` DATETIME NOT NULL,
	`GiftImportId` VARCHAR(255) NULL,
	`AccountName` VARCHAR(255) NOT NULL,
	`PaymentMethod` VARCHAR(255) NOT NULL,
	`GiftType` VARCHAR(255) NOT NULL,
	`Amount` DECIMAL(18,2) NOT NULL,
	`Fund` VARCHAR(255) NOT NULL,
	`Intern` VARCHAR(255) NULL,
	`PrimaryAddressee` VARCHAR(255) NULL,
	`SoftCreditName` VARCHAR(255) NULL,
	`HonorMemorialName` VARCHAR(255) NULL,
	`IsAnonymous` BOOLEAN NOT NULL DEFAULT FALSE,
	`Frequency` TINYINT NULL,
	`DateCreated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY (`Id`),
	KEY `ix_DonationData_DonorDate` (`DonorId`, `Date`),
	KEY `ix_DonationData_FundDate` (`Fund`, `Date`),
	KEY `ix_DonationData_GiftImportId` (`GiftImportId`),
	KEY `ix_DonationData_DuplicateCandidate`
		(`GiftImportId`(100), `Fund`(100), `Date`, `Amount`, `PaymentMethod`(100), `GiftType`(100), `DonorId`),
	CONSTRAINT `fk_DonationData_Donor`
		FOREIGN KEY (`DonorId`) REFERENCES `Donors` (`Id`)
		ON UPDATE CASCADE
		ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Rollback storage retains canonical donation rows, not duplicate source rows.
-- It has no foreign keys so a rollback remains possible after donor records are
-- rebuilt or reassigned.
CREATE TABLE `DonationDataBackup`
(
	`BackupId` CHAR(36) NOT NULL,
	`Id` INT NOT NULL,
	`DonorId` BIGINT NULL,
	`Date` DATETIME NULL,
	`GiftImportId` VARCHAR(255) NULL,
	`AccountName` VARCHAR(255) NULL,
	`PaymentMethod` VARCHAR(255) NULL,
	`GiftType` VARCHAR(255) NULL,
	`Amount` DECIMAL(18,2) NULL,
	`Fund` VARCHAR(255) NULL,
	`Intern` VARCHAR(255) NULL,
	`PrimaryAddressee` VARCHAR(255) NULL,
	`SoftCreditName` VARCHAR(255) NULL,
	`HonorMemorialName` VARCHAR(255) NULL,
	`IsAnonymous` BOOLEAN NULL DEFAULT FALSE,
	`Frequency` TINYINT NULL,
	`DateCreated` DATETIME NULL,
	`BackupAt` DATETIME NOT NULL,
	`Pinned` BOOLEAN NOT NULL DEFAULT FALSE,
	`SourceRangeStart` DATETIME NULL,
	PRIMARY KEY (`BackupId`, `Id`),
	KEY `ix_DonationDataBackup_BackupAt` (`BackupAt`),
	KEY `ix_DonationDataBackup_Pinned` (`Pinned`),
	KEY `ix_DonationDataBackup_BackupId` (`BackupId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `DonorContacts`
(
	`Id` BIGINT NOT NULL AUTO_INCREMENT,
	`DonorId` BIGINT NOT NULL,
	`ContactType` VARCHAR(32) NOT NULL,
	`ContactValue` VARCHAR(500) NOT NULL,
	`NormalizedValue` VARCHAR(500) NOT NULL,
	`DateCreated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
	`DateModified` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
	PRIMARY KEY (`Id`),
	UNIQUE KEY `ux_DonorContacts_DonorTypeValue`
		(`DonorId`, `ContactType`, `NormalizedValue`),
	KEY `ix_DonorContacts_DonorType` (`DonorId`, `ContactType`),
	CONSTRAINT `fk_DonorContacts_Donor`
		FOREIGN KEY (`DonorId`) REFERENCES `Donors` (`Id`)
		ON UPDATE CASCADE
		ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- ContactType values currently used by the importer:
--   Email
--   HomePhone
--   MobilePhone
--   AddressLine1
--   City
--   State
--   PostalCode
--   Country
--
-- The application owns normalization and validation of ContactType and
-- NormalizedValue. Contact rows represent only current values; do not add
-- primary/secondary columns or contact history fields here.

-- Optional verification queries:
-- SHOW CREATE TABLE `Donors`;
-- SHOW CREATE TABLE `DonationData`;
-- SHOW CREATE TABLE `DonorContacts`;
-- SELECT COUNT(*) FROM `DonationData`;
-- SELECT COUNT(*) FROM `Donors`;
-- SELECT COUNT(*) FROM `DonorContacts`;
