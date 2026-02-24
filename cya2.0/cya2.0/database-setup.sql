-- CYA Database Setup Script
-- Run these commands in your MySQL database to set up authentication

-- 1. Create the database (if it doesn't exist)
CREATE DATABASE IF NOT EXISTS cya;
USE cya;

-- 2. Create Users table (if it doesn't exist)
CREATE TABLE IF NOT EXISTS Users (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    GoogleId VARCHAR(255) DEFAULT NULL,
    Email VARCHAR(255) UNIQUE NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Language VARCHAR(10) DEFAULT 'en',
    AuthLevel ENUM('User', 'Viewer', 'Admin') DEFAULT 'User',
    DefaultAccount INT DEFAULT NULL,
    Prefrence VARCHAR(50) DEFAULT 'default',
    DateCreated TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 3. Add your user as an admin user
INSERT INTO Users (Email, Name, Language, AuthLevel, DefaultAccount) 
VALUES ('srenderfrance@gmail.com', 'S. Render', 'en', 'Admin', NULL)
ON DUPLICATE KEY UPDATE 
    Name = VALUES(Name),
    AuthLevel = VALUES(AuthLevel);

-- 4. Create Accounts table (if it doesn't exist) for managing fundraising accounts
CREATE TABLE IF NOT EXISTS Accounts (
    AccountId INT AUTO_INCREMENT PRIMARY KEY,
    Fund VARCHAR(255) NOT NULL,
    AccountingClass VARCHAR(255) NOT NULL,
    AccountNumber VARCHAR(50),
    Overhead DECIMAL(5,2) DEFAULT 0.00,
    SoftCredit VARCHAR(255),
    BalanceAdjustment DECIMAL(15,2) DEFAULT 0.00,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_fund (Fund)
);

-- 5. Create AccountsUsers table for user-account relationships
CREATE TABLE IF NOT EXISTS AccountsUsers (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    UserId INT NOT NULL,
    AccountId INT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (AccountId) REFERENCES Accounts(AccountId) ON DELETE CASCADE,
    UNIQUE KEY unique_user_account (UserId, AccountId)
);

-- 6. Example: Create a sample account (optional)
INSERT INTO Accounts (Fund, AccountingClass, AccountNumber, Overhead, BalanceAdjustment)
VALUES ('General Operations', 'GEN-001', '1000-01', 12.00, 0.00)
ON DUPLICATE KEY UPDATE 
    AccountingClass = VALUES(AccountingClass),
    AccountNumber = VALUES(AccountNumber);

-- 7. Check your setup
SELECT 'Database Setup Complete' as Status;
SELECT * FROM Users WHERE Email = 'srenderfrance@gmail.com';
SELECT * FROM Accounts;

-- 8. Notes:
-- - Your email 'srenderfrance@gmail.com' is now added as an Admin user
-- - The GoogleId will be populated automatically when you first log in
-- - AuthLevel options: 'User' (limited access), 'Viewer' (read-only), 'Admin' (full access)
-- - You can add more accounts and assign users to them using AccountsUsers table-- - You can add more accounts and assign users to them using AccountsUsers table