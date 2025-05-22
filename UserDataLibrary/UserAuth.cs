using System.Data;
using ModelsLibrary;
using DataLibrary;
using System.Threading.Tasks;


namespace UserAuth
{
    public class UserRepository
    {
        private readonly IDataAccess _dataAccess;
        private readonly string _connectionString;

        public UserRepository(IDataAccess dataAccess, string connectionString)
        {
            _dataAccess = dataAccess;
            _connectionString = connectionString;
        }
        public async Task<User?> GetUserByGoogleIdAsync(string googleId)
        {
            var sql = "SELECT * FROM Users WHERE GoogleId = @GoogleId;";
            var parameters = new { GoogleId = googleId };

            var users = await _dataAccess.LoadData<User, dynamic>(sql, parameters, _connectionString);
            return users.FirstOrDefault(); // Return the first user or null if not found
        }
        public async Task CreateUserAsync(string email, string name)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));
            }

            var sql = @"
        INSERT INTO Users (Email, Name, Language, AuthLevel, DefaultAccount)
        VALUES (@Email, @Name, 'en', 'User', NULL)
        ON DUPLICATE KEY UPDATE
            Name = VALUES(Name),
            Email = VALUES(Email);";

            var parameters = new
            {
                Email = email,
                Name = name
            };

            await _dataAccess.SaveData(sql, parameters, _connectionString);
        }
        public async Task<int> CompleteUserRegistrationAsync(string googleId, string email, string name)
        {
            if (string.IsNullOrWhiteSpace(googleId))
            {
                throw new ArgumentException("GoogleId cannot be null or empty.", nameof(googleId));
            }
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Name cannot be null or empty.", nameof(name));
            }

            var sql = @"
            UPDATE Users
            SET GoogleId = @GoogleId
            WHERE Email = @Email;";

            var parameters = new
            {
                GoogleId = googleId,
                Email = email,
                Name = name
            };

            var rowsAffected = await _dataAccess.SaveData(sql, parameters, _connectionString);
            return rowsAffected; // Return the number of rows affected
        }


    }
}
