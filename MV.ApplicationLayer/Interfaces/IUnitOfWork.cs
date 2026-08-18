using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.ApplicationLayer.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRepository UserRepository { get; }

        ITutorRepository TutorRepository { get; }
        IStudentRepository StudentRepository { get; }

        // Tutor Search Repository
        ITutorSearchRepository TutorSearchRepository { get; }

        // Notification Repository
        INotificationRepository NotificationRepository { get; }

        IRefreshTokenRepository RefreshTokenRepository { get; }

        IStaffPermissionRepository StaffPermissionRepository { get; }

        // Question bank (AI giải bài tập)
        IQuestionRepository QuestionRepository { get; }
        ISourceDocumentRepository SourceDocumentRepository { get; }

        IAssessmentRepository AssessmentRepository { get; }

        // Single commit point
        Task<int> SaveChangesAsync();
    }
}
