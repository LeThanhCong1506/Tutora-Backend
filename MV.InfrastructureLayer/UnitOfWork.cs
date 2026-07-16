using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AgoraDbContext _context;
        private readonly IPasswordRepository _passwordRepository;
        private readonly ILogger<UnitOfWork> _logger;

        // Repository fields should be of the INTERFACE type
        private IUserRepository? _userRepository;

        private ITutorRepository? _tutorRepository;
        private IStudentRepository? _studentRepository;
        private ITutorSearchRepository? _tutorSearchRepository;
        private INotificationRepository? _notificationRepository;
        private IRefreshTokenRepository? _refreshTokenRepository;
        private IStaffPermissionRepository? _staffPermissionRepository;
        private IQuestionRepository? _questionRepository;
        private ISourceDocumentRepository? _sourceDocumentRepository;

        // Expose repository INTERFACES
        public IUserRepository UserRepository =>
            _userRepository ??= new UserRepository(_context, _passwordRepository);

        public ITutorRepository TutorRepository =>
            _tutorRepository ??= new TutorRepository(_context);
        public IStudentRepository StudentRepository =>
            _studentRepository ??= new StudentRepository(_context);

        public ITutorSearchRepository TutorSearchRepository =>
            _tutorSearchRepository ??= new TutorSearchRepository(_context);

        public INotificationRepository NotificationRepository =>
            _notificationRepository ??= new NotificationRepository(_context);

        public IRefreshTokenRepository RefreshTokenRepository =>
            _refreshTokenRepository ??= new RefreshTokenRepository(_context);

        public IStaffPermissionRepository StaffPermissionRepository =>
            _staffPermissionRepository ??= new StaffPermissionRepository(_context);

        public IQuestionRepository QuestionRepository =>
            _questionRepository ??= new QuestionRepository(_context);

        public ISourceDocumentRepository SourceDocumentRepository =>
            _sourceDocumentRepository ??= new SourceDocumentRepository(_context);

        //CONSTRUCTOR INJECTION for DbContext and dependencies
        public UnitOfWork(AgoraDbContext context, IPasswordRepository passwordRepository, ILogger<UnitOfWork> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _passwordRepository = passwordRepository ?? throw new ArgumentNullException(nameof(passwordRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> SaveChangesAsync()
        {
            try
            {
                return await _context.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                var innerMessage = e.InnerException?.Message ?? e.Message;
                _logger.LogError(e, "[UnitOfWork] DbUpdateException: {InnerMessage}", innerMessage);
                throw new DbUpdateException($"[DB ERROR] {innerMessage}", e.InnerException ?? e);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UnitOfWork] Unexpected exception during SaveChangesAsync");
                throw;
            }
        }

        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
