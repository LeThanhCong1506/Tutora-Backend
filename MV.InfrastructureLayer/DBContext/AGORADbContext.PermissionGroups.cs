using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;

namespace MV.InfrastructureLayer.DBContext;

public partial class AgoraDbContext
{
    public virtual DbSet<PermissionDefinition> PermissionDefinitions { get; set; }
    public virtual DbSet<PermissionDefinitionRequirement> PermissionDefinitionRequirements { get; set; }
    public virtual DbSet<PermissionGroup> PermissionGroups { get; set; }
    public virtual DbSet<PermissionGroupPermission> PermissionGroupPermissions { get; set; }
    public virtual DbSet<StaffPermissionGroupAssignment> StaffPermissionGroupAssignments { get; set; }
    public virtual DbSet<PermissionAuditLog> PermissionAuditLogs { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PermissionDefinition>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("permission_definitions_pkey");
            entity.ToTable("permission_definitions");
            entity.Property(e => e.Key).HasMaxLength(100).HasColumnName("permission_key");
            entity.Property(e => e.Domain).HasMaxLength(100).HasColumnName("domain");
            entity.Property(e => e.Module).HasMaxLength(100).HasColumnName("module");
            entity.Property(e => e.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.Label).HasMaxLength(200).HasColumnName("label");
        });

        modelBuilder.Entity<PermissionDefinitionRequirement>(entity =>
        {
            entity.HasKey(e => new { e.PermissionKey, e.RequiredPermissionKey }).HasName("permission_definition_requirements_pkey");
            entity.ToTable("permission_definition_requirements");
            entity.Property(e => e.PermissionKey).HasMaxLength(100).HasColumnName("permission_key");
            entity.Property(e => e.RequiredPermissionKey).HasMaxLength(100).HasColumnName("required_permission_key");
            entity.HasOne(e => e.PermissionDefinition)
                .WithMany(e => e.Requirements)
                .HasForeignKey(e => e.PermissionKey)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("permission_definition_requirements_permission_fkey");
            entity.HasOne(e => e.RequiredPermissionDefinition)
                .WithMany()
                .HasForeignKey(e => e.RequiredPermissionKey)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("permission_definition_requirements_required_fkey");
        });
        modelBuilder.Entity<PermissionGroup>(entity =>
        {
            entity.HasKey(e => e.PermissionGroupId).HasName("permission_groups_pkey");
            entity.ToTable("permission_groups");
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
            entity.Property(e => e.Description).HasMaxLength(255).HasColumnName("description");
            entity.Property(e => e.Version).IsConcurrencyToken().HasColumnName("version");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
            entity.Property(e => e.CreatedBy).HasMaxLength(50).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedBy).HasMaxLength(50).HasColumnName("updated_by");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamp without time zone").HasColumnName("deleted_at");
        });

        modelBuilder.Entity<PermissionGroupPermission>(entity =>
        {
            entity.HasKey(e => new { e.PermissionGroupId, e.PermissionKey }).HasName("permission_group_permissions_pkey");
            entity.ToTable("permission_group_permissions");
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
            entity.Property(e => e.PermissionKey).HasMaxLength(100).HasColumnName("permission_key");
            entity.HasOne(e => e.PermissionGroup)
                .WithMany(e => e.Permissions)
                .HasForeignKey(e => e.PermissionGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("permission_group_permissions_group_fkey");
            entity.HasOne(e => e.PermissionDefinition)
                .WithMany(e => e.PermissionGroups)
                .HasForeignKey(e => e.PermissionKey)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("permission_group_permissions_definition_fkey");
        });

        modelBuilder.Entity<StaffPermissionGroupAssignment>(entity =>
        {
            entity.HasKey(e => e.StaffUserId).HasName("staff_permission_group_assignments_pkey");
            entity.ToTable("staff_permission_group_assignments");
            entity.Property(e => e.StaffUserId).HasMaxLength(50).HasColumnName("staff_user_id");
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
            entity.Property(e => e.Version).IsConcurrencyToken().HasColumnName("version");
            entity.Property(e => e.UpdatedBy).HasMaxLength(50).HasColumnName("updated_by");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.HasIndex(e => e.PermissionGroupId, "idx_staff_permission_group_assignments_group");
            entity.HasOne(e => e.StaffUser)
                .WithOne()
                .HasForeignKey<StaffPermissionGroupAssignment>(e => e.StaffUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("staff_permission_group_assignments_staff_fkey");
            entity.HasOne(e => e.PermissionGroup)
                .WithMany(e => e.StaffAssignments)
                .HasForeignKey(e => e.PermissionGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("staff_permission_group_assignments_group_fkey");
        });

        modelBuilder.Entity<PermissionAuditLog>(entity =>
        {
            entity.HasKey(e => e.PermissionAuditLogId).HasName("permission_audit_logs_pkey");
            entity.ToTable("permission_audit_logs");
            entity.Property(e => e.PermissionAuditLogId).UseIdentityAlwaysColumn().HasColumnName("permission_audit_log_id");
            entity.Property(e => e.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.EntityType).HasMaxLength(50).HasColumnName("entity_type");
            entity.Property(e => e.EntityId).HasMaxLength(100).HasColumnName("entity_id");
            entity.Property(e => e.PermissionGroupId).HasColumnName("permission_group_id");
            entity.Property(e => e.StaffUserId).HasMaxLength(50).HasColumnName("staff_user_id");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.ActorUserId).HasMaxLength(50).HasColumnName("actor_user_id");
            entity.Property(e => e.DetailsJson).HasColumnType("jsonb").HasColumnName("details_json");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.HasIndex(e => e.PermissionGroupId, "idx_permission_audit_logs_group");
            entity.HasIndex(e => e.StaffUserId, "idx_permission_audit_logs_staff");
        });
    }
}
