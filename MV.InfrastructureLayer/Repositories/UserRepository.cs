using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.InfrastructureLayer.DBContext;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AgoraDbContext _context;
        private readonly IPasswordRepository _passwordRepository;

        public UserRepository(AgoraDbContext context, IPasswordRepository passwordRepository)
        {
            _context = context;
            _passwordRepository = passwordRepository;
        }

        public async Task<PagedList<User>> GetUsersAsync(UserParameters parameters)
        {
            // 1. Khởi tạo Queryable và sử dụng AsNoTracking để tối ưu hiệu suất đọc (Read-only)
            var query = _context.Users.AsNoTracking().AsQueryable();



            // 3. Sorting (Sắp xếp)
            query = parameters.OrderBy switch
            {
                UserListSortBy.FullName => query.OrderBy(u => u.Fullname),
                UserListSortBy.FullNameDesc => query.OrderByDescending(u => u.Fullname),
                UserListSortBy.Email => query.OrderBy(u => u.Email),
                UserListSortBy.CreatedAt => query.OrderBy(u => u.Createdat),
                _ => query.OrderByDescending(u => u.Createdat) // Default: Mới nhất lên đầu
            };

            // 4. Pagination (Phân trang) & Trả về PagedList
            return PagedList<User>.ToPagedList(query, parameters.PageNumber, parameters.PageSize);
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Userid == userId);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<User?> GetUserByPhoneAsync(string phone)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Phone == phone);
        }

        public async Task<User?> GetUserByZaloIdAsync(string zaloUserId)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Zalouserid == zaloUserId);
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        }

        public async Task<PagedList<User>> GetUsersByRoleAsync(string roleName, UserParameters parameters)
        {
            var query = _context.Users
                .Where(u => u.Primaryrole != null && u.Primaryrole.ToLower() == roleName.ToLower())
                .AsNoTracking()
                .AsQueryable();



            return PagedList<User>.ToPagedList(query, parameters.PageNumber, parameters.PageSize);
        }

        public async Task<PagedList<User>> GetTutorsBySubjectAsync(int subjectId, UserParameters parameters)
        {
            var query = _context.Users
                .Include(u => u.Tutorprofile).ThenInclude(tp => tp.Tutorsubjectgradeprices)
                .Where(u => u.Primaryrole != null && u.Primaryrole.ToLower() == UserRole.Tutor.ToLower() &&
                            u.Tutorprofile.Tutorsubjectgradeprices.Any(ts => ts.Subjectid == subjectId && ts.Isactive))
                .AsNoTracking()
                .AsQueryable();

            return PagedList<User>.ToPagedList(query, parameters.PageNumber, parameters.PageSize);
        }

        public async Task<PagedList<User>> GetPendingTutorsAsync(UserParameters parameters)
        {
            // Lấy User có Role là Tutor VÀ trạng thái Profile là 'pending_approval'
            var query = _context.Users
                .Include(u => u.Tutorprofile)
                .Where(u => u.Primaryrole != null && u.Primaryrole.ToLower() == UserRole.Tutor.ToLower() &&
                            u.Tutorprofile.Profilestatus.ToLower() == TutorProfileStatus.PendingApproval)
                .AsNoTracking();



            // Sorting - Mặc định sắp xếp theo ngày tạo mới nhất
            query = parameters.OrderBy?.ToLower() switch
            {
                UserListSortBy.CreatedAtAsc => query.OrderBy(u => u.Createdat),
                UserListSortBy.FullNameAsc => query.OrderBy(u => u.Fullname),
                UserListSortBy.FullNameDesc => query.OrderByDescending(u => u.Fullname),
                _ => query.OrderByDescending(u => u.Createdat) // default: newest first
            };

            return PagedList<User>.ToPagedList(query, parameters.PageNumber, parameters.PageSize);
        }

        public async Task<List<Studentprofile>> GetStudentProfilesByParentIdAsync(string parentId)
        {
            // Lấy hồ sơ học sinh từ bảng Studentprofiles dựa trên ParentID
            return await _context.Studentprofiles
                .Where(sp => sp.Parentid == parentId)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Tutorprofile?> GetTutorProfileByIdAsync(string tutorId)
        {
            return await _context.Tutorprofiles
                .Include(tp => tp.Tutorsubjectgradeprices)
                .FirstOrDefaultAsync(tp => tp.Tutorid == tutorId);
        }

        // --- Validation Helpers ---

        public async Task<bool> IsEmailUniqueAsync(string email)
        {
            return !await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> IsUsernameUniqueAsync(string username)
        {
            return !await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower());
        }

        public async Task<bool> IsPhoneUniqueAsync(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return true;
            return !await _context.Users.AnyAsync(u => u.Phone == phone);
        }

        public async Task<bool> IsIdentityNumberUniqueAsync(string identityNumber)
        {
            return !await _context.Users.AnyAsync(u => u.Identitynumber == identityNumber);
        }

        public async Task<bool> SubjectExistsAsync(int subjectId)
        {
            return await _context.Subjects.AnyAsync(s => s.Subjectid == subjectId);
        }

        public async Task<bool> HasTutorAlreadySelectedSubjectAsync(string tutorId, int subjectId)
        {
            return await _context.Tutorsubjectgradeprices.AnyAsync(ts => ts.Tutorid == tutorId && ts.Subjectid == subjectId && ts.Isactive);
        }

        public async Task<bool> CheckIfTutorHasAnySubjectAsync(string tutorId)
        {
            return await _context.Tutorsubjectgradeprices.AnyAsync(ts => ts.Tutorid == tutorId && ts.Isactive);
        }

        public async Task<bool> CheckIfTutorHasAnyAvailabilityAsync(string tutorId)
        {
            return await _context.Tutoravailabilities.AnyAsync(ta => ta.Tutorid == tutorId);
        }

        // --- CRUD Operations ---

        public async Task CreateUserAsync(User user)
        {
            // Set default values nếu chưa có
            user.Createdat ??= MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            user.Status ??= 1; // 1: Active

            await _context.Users.AddAsync(user);
            // Lưu ý: SaveChangesAsync sẽ được gọi ở UnitOfWork hoặc Service, 
            // nhưng nếu muốn Repo tự handle thì gọi ở đây. 
            // Tuy nhiên, theo Pattern UnitOfWork, Repo chỉ Add vào DbSet.
        }

        public async Task CreateTutorSubjectAsync(Tutorsubject tutorSubject)
        {
            var defaultGrade = await _context.Gradelevels.OrderBy(g => g.Levelorder).FirstOrDefaultAsync();
            if (defaultGrade == null || tutorSubject.Subjectid == null || string.IsNullOrWhiteSpace(tutorSubject.Tutorid)) return;

            await _context.Tutorsubjectgradeprices.AddAsync(new Tutorsubjectgradeprice
            {
                Tutorid = tutorSubject.Tutorid,
                Subjectid = tutorSubject.Subjectid.Value,
                Gradelevelid = defaultGrade.Gradelevelid,
                Priceperhour = 0,
                Currency = "VND",
                Isactive = false,
                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now,
                Updatedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
            });
        }

        public async Task CreateNewAvailabilitySlotsAsync(List<Tutoravailability> newSlots)
        {
            await _context.Tutoravailabilities.AddRangeAsync(newSlots);
        }

        public Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            return Task.CompletedTask;
        }

        public async Task UpdateLastLoginAtAsync(string userId, DateTime time)
        {
            await _context.Users
                .Where(u => u.Userid == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Lastloginat, time));
        }

        public async Task<bool> UpdateFcmTokenAsync(string userId, string fcmToken)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Userid == userId);
            if (user == null) return false;

            user.Fcmtoken = string.IsNullOrWhiteSpace(fcmToken) ? null : fcmToken;
            _context.Users.Update(user);
            return true;
        }

        public Task UpdateTutorProfileAsync(Tutorprofile profile)
        {
            _context.Tutorprofiles.Update(profile);
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(User user)
        {
            // Soft Delete: Không xóa khỏi DB, chỉ đổi Status
            // Giả sử Status = 0 là Deleted/Banned
            user.Status = 0;
            _context.Users.Update(user);

            return Task.CompletedTask;
        }

        public async Task DeleteAllExistingAvailabilityByTutorIdAsync(string tutorId)
        {
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId)
                .ToListAsync();
            _context.Tutoravailabilities.RemoveRange(existingSlots);
        }

        public void Detach(User user)
        {
            _context.Entry(user).State = EntityState.Detached;
        }

        public async Task<string?> GetUserRoleByIdAsync(string userId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Userid == userId);
            return user?.Primaryrole;
        }

        public async Task<bool> CheckIfUserLoginCorrectAsync(string email, string password)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            if (user == null)
            {
                return false; // User không tồn tại
            }
            // Sử dụng PasswordRepository để kiểm tra mật khẩu
            return _passwordRepository.VerifyPassword(password, user.Password);
        }

        public async Task<bool> CheckIfUserLoginCorrectByPhoneAsync(string phone, string password)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Phone == phone);
            if (user == null)
            {
                return false; // User không tồn tại
            }
            // Sử dụng PasswordRepository để kiểm tra mật khẩu
            return _passwordRepository.VerifyPassword(password, user.Password);
        }

        public async Task<bool> CheckIfUserLoginCorrectByUsernameAsync(string username, string password)
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username != null && u.Username.ToLower() == username.ToLower());
            if (user == null)
            {
                return false;
            }
            return _passwordRepository.VerifyPassword(password, user.Password);
        }

        public async Task<User?> GetUserByParentCodeAsync(string parentCode)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Parentcode == parentCode
                    && u.Parentcodeexpiresat != null
                    && u.Parentcodeexpiresat > MV.DomainLayer.Helpers.VietnamTimeHelper.Now);
        }
    }
}
