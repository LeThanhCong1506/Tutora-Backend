using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces
{
    public interface ITutorRepository
    {
        Task<Tutorprofile?> GetTutorProfileByIdAsync(string tutorId);
        Task<Tutorprofile?> GetTutorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
        Task<List<Tutorsubject>> GetTutorSubjectsByTutorIdAsync(string tutorId);
        Task<List<Tutorsubjectgradeprice>> GetTutorSubjectGradePricesAsync(string tutorId);
        void DeleteTutorSubjects(IEnumerable<Tutorsubject> subjects);
        Task CreateTutorSubjectsAsync(IEnumerable<Tutorsubject> subjects);
        Task ReplaceTutorSubjectGradePricesAsync(string tutorId, IEnumerable<Tutorsubjectgradeprice> prices);
        Task UpdateTutorProfileStatusAsync(Tutorprofile profile);
        Task UpdateTutorProfileAsync(Tutorprofile profile);
        Task<List<Tutorpackage>> GetTutorPackagesAsync(string tutorId, bool includeInactive = false);
        Task<Tutorpackage?> GetTutorPackageAsync(string tutorId, int packageId);
        Task AddTutorPackageAsync(Tutorpackage package);

        // Subject validation
        Task<List<int>> GetExistingSubjectIdsAsync(List<int> subjectIds);
        Task<List<int>> GetExistingGradeLevelIdsAsync(List<int> gradeLevelIds);

        // Bank verification methods
        Task<List<BankChangeLog>> GetBankChangeLogsByTutorIdAsync(string tutorId, int limit = 10, CancellationToken cancellationToken = default);
        Task AddBankChangeLogAsync(BankChangeLog log, CancellationToken cancellationToken = default);
        Task<int> CountBankChangesInLastMonthAsync(string tutorId, CancellationToken cancellationToken = default);
        Task<Tutorprofile?> GetTutorByVerificationCodeAsync(string? verificationCode, CancellationToken cancellationToken = default);

        // Certificate methods
        Task<List<Tutorcertificate>> GetCertificatesByTutorIdAsync(string tutorId);
        Task<Tutorcertificate?> GetCertificateByIdAsync(string certificateId);
        Task AddCertificateAsync(Tutorcertificate certificate);
        void DeleteCertificate(Tutorcertificate certificate);
    }
}
