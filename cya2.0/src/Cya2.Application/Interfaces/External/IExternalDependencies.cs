using DataLibrary; // Main application's IDataAccess
using ModelsLibrary;
using UserAuth;
using cya2.Services.Imports; // Main application's import types

namespace Cya2.Application.Interfaces.External;

// Use the actual import service interfaces from the main application
// These are in cya2.Services.Imports namespace

// External import services that exist in main application
// Note: These interfaces already exist in main application so we just reference them

// Bridge to external UserRepository from UserAuth library
public interface IExternalUserRepository
{
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByGoogleIdAsync(string googleId);
    Task<bool> ValidateGoogleIdAsync(string email, string googleId);
    Task CreateUserAsync(string email, string name);
    Task<int> CompleteUserRegistrationAsync(string googleId, string email, string name);
    Task<bool> UpdateUserAuthLevelAsync(int userId, string authLevel);
}