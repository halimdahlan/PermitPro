using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using PermitPro.Core.Entities;
using PermitPro.Core.Interfaces;

using System.Reflection;

namespace PermitPro.Core.Data;


public class UserRole : IdentityUserRole<string>
{
	public virtual UserInfo? User { get; set; }
	public virtual Role? Role { get; set; }
}


public class Role : IdentityRole, ISoftDeletable
{
	public virtual List<UserRole>? UserRoles { get; set; }
	public virtual List<SystemMenu>? SystemMenus { get; set; }

	public string? Description { get; set; }
	public bool IsSystemRole { get; set; }
	public bool IsUnlimitedUsers { get; set; }
	public DateTime CreatedWhen { get; set; }
	public DateTime? UpdatedWhen { get; set; }
	public Guid? CreatedBy { get; set; }
	public Guid? UpdatedBy { get; set; }
	public bool IsDeleted { get; set; }
	public DateTime? DeletedWhen { get; set; }
	public Guid? DeletedBy { get; set; }
}


/// <summary>
/// Application database context used for the app
/// </summary>
public class ApplicationDbContext
	 : IdentityDbContext<UserInfo, Role, string, IdentityUserClaim<string>, UserRole, IdentityUserLogin<string>, IdentityRoleClaim<string>, IdentityUserToken<string>>, IDataProtectionKeyContext
{
	/// <summary>
	/// The default constructor
	/// </summary>
	/// <param name="options">Database context options</param>
	public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
	{
	}

	public DbSet<Company> Companies { get; set; }

	public DbSet<Site> Sites { get; set; }

	public DbSet<Division> Divisions { get; set; }

	public DbSet<Department> Departments { get; set; }

	public DbSet<Address> Addresses { get; set; }

	public DbSet<Contact> Contacts { get; set; }

	public DbSet<Permit> Permits { get; set; }

	//public DbSet<SitePermit> SitesPermits { get; set; }

	public DbSet<AuditLog> AuditLogs { get; set; }

	public DbSet<Workflow> Workflows { get; set; }

	public DbSet<WorkflowStep> WorkflowSteps { get; set; }

	public DbSet<WorkflowHistory> WorkflowHistories { get; set; }

	public DbSet<Attachment> Attachments { get; set; }

	public DbSet<Certificate> Certificates { get; set; }

	public DbSet<Notification> Notifications { get; set; }

	public DbSet<PermitNumber> PermitNumbers { get; set; }

	public DbSet<SystemMenu> SystemMenus { get; set; }

	public DbSet<AppSettingCategory> AppSettingCategories { get; set; }

	public DbSet<AppSetting> AppSettings { get; set; }

	public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

	public DbSet<FormControl> FormControls { get; set; }

	public DbSet<FormTemplate> FormTemplates { get; set; }

	/// <summary>
	/// Flag to control delete behavior. When TRUE (default), soft-deletes are used.
	/// When FALSE, hard deletes (permanent removal) are performed.
	/// </summary>
	public bool UseSoftDelete { get; set; } = true;


	/// <summary>
	/// An override on model creating
	/// </summary>
	/// <param name="builder">Model builder</param>
	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);

		builder.Entity<UserInfo>(options =>
		{
			options.HasMany(x => x.UserRoles)
					.WithOne(x => x.User)
					.HasForeignKey(x => x.UserId)
					.IsRequired();

			options.HasMany(x => x.Sites)
					.WithMany(x => x.Users);

			options.HasOne(x => x.UserCompany);

			options.HasMany(x => x.UserNotifications)
					.WithOne(x => x.NotificationUser)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.WorkflowSteps)
					.WithMany(x => x.Approvers)
					.UsingEntity(x => x.ToTable("ApproversWorkflowSteps"));

			options.HasMany(x => x.AuditLogs)
					.WithOne(x => x.AuditLogUser)
					.OnDelete(DeleteBehavior.Cascade);

		});

		builder.Entity<Role>(options =>
		{
			options.HasMany(x => x.UserRoles)
					.WithOne(x => x.Role)
					.HasForeignKey(x => x.RoleId)
					.IsRequired();
		});

		builder.Entity<Site>(options =>
		{
			options.HasMany(x => x.Users)
					.WithMany(x => x.Sites);
		});

		builder.Entity<Site>()
			 .HasMany(x => x.Permits)
			 .WithOne(x => x.Site)
			 .HasForeignKey("SiteId")
			 .HasPrincipalKey(x => x.Id)
			 .OnDelete(DeleteBehavior.NoAction);

		builder.Entity<Company>(options =>
		{
			options.HasMany(x => x.CompanyAddresses)
					.WithOne(x => x.AddressCompany)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.CompanySites)
					.WithOne(x => x.SiteCompany)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.CompanyWorkflows)
					.WithOne(x => x.WorkflowCompany)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.CompanyUsers)
					.WithOne(x => x.UserCompany);

			options.HasMany(x => x.CompanyPermits)
					.WithOne(x => x.Company);

		});

		builder.Entity<Permit>(options =>
		{
			options.HasMany(x => x.Attachments)
					.WithOne(x => x.Permit)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.WorkflowHistories)
					.WithOne(x => x.Permit)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.Certificates)
					.WithMany(x => x.Permits)
					.UsingEntity(x => x.ToTable("PermitsCertificates"));
		});

		builder.Entity<Workflow>(options =>
		{
			options.HasMany(x => x.WorkflowSteps)
					.WithOne(x => x.WorkflowStepWorkflow)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.WorkflowHistories)
					.WithOne(x => x.HistoryWorkflow)
					.OnDelete(DeleteBehavior.Cascade);

			options.HasMany(x => x.WorkflowPermits)
				.WithOne(x => x.PermitWorkflow)
				.OnDelete(DeleteBehavior.NoAction);
		});

		builder.Entity<SystemMenu>(options =>
		{
			options.HasMany(x => x.Roles)
					.WithMany(x => x.SystemMenus)
					.UsingEntity(x => x.ToTable("SystemMenusRoles"));
		});

		builder.Entity<Address>();

		builder.Entity<Contact>();

		builder.Entity<FormControl>(options =>
		{
			options.HasOne(x => x.Company)
				   .WithMany()
				   .HasForeignKey(x => x.CompanyId)
				   .OnDelete(DeleteBehavior.Cascade);
		});

		builder.Entity<FormTemplate>(options =>
		{
			options.HasOne(x => x.Company)
				   .WithMany()
				   .HasForeignKey(x => x.CompanyId)
				   .OnDelete(DeleteBehavior.Cascade);

			// Enforce one active template per company+type at the application layer;
			// filtered unique index covers only active=true rows.
			options.HasIndex(x => new { x.CompanyId, x.TemplateType, x.IsActive })
				   .IsUnique()
				   .HasFilter("[IsActive] = 1")
				   .HasDatabaseName("IX_FormTemplates_CompanyId_TemplateType_Active");
		});

		builder.Entity<AppSettingCategory>(options =>
		{
			options.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
			// EF's auto-generated filter for the index above excludes CompanyId IS NULL rows
			// (nullable-column convention), so global categories need their own uniqueness guard.
			options.HasIndex(x => x.Name)
				   .IsUnique()
				   .HasFilter("[CompanyId] IS NULL")
				   .HasDatabaseName("IX_AppSettingCategories_Name_Global");
			options.HasMany(x => x.Settings)
				   .WithOne(x => x.Category)
				   .HasForeignKey(x => x.CategoryId)
				   .OnDelete(DeleteBehavior.Cascade);
		});

		builder.Entity<AppSetting>(options =>
		{
			options.HasIndex(x => new { x.CompanyId, x.CategoryId, x.Key }).IsUnique();
			// Same gap as above: global settings need a dedicated filtered unique index.
			options.HasIndex(x => new { x.CategoryId, x.Key })
				   .IsUnique()
				   .HasFilter("[CompanyId] IS NULL")
				   .HasDatabaseName("IX_AppSettings_CategoryId_Key_Global");
			// Preserve the plain (unfiltered) FK lookup index — EF's convention-generated
			// index would otherwise be dropped as "redundant" with the filtered index above,
			// but that one only covers global rows, not a CategoryId scan across all companies.
			options.HasIndex(x => x.CategoryId)
				   .HasDatabaseName("IX_AppSettings_CategoryId");
		});

		// UserRole is the required dependent end of UserInfo; it must share the same IsDeleted
		// filter so that Identity role queries automatically exclude soft-deleted users.
		builder.Entity<UserRole>().HasQueryFilter(ur => !ur.User!.IsDeleted);

		// Apply HasQueryFilter(!IsDeleted) to every entity that implements ISoftDeletable.
		// Uses a static generic helper so EF can build the expression tree correctly.
		foreach (var entityType in builder.Model.GetEntityTypes())
		{
			if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
			{
				var method = typeof(ApplicationDbContext)
					 .GetMethod(nameof(ApplySoftDeleteFilter), BindingFlags.Static | BindingFlags.NonPublic)!
					 .MakeGenericMethod(entityType.ClrType);
				method.Invoke(null, [builder]);
			}
		}
	}

	private static void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : class, ISoftDeletable
	{
		builder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);
	}


	// -------------------------------------------------------------------------
	// Soft-delete with cascade
	// -------------------------------------------------------------------------

	/// <summary>
	/// Soft-deletes an entity and cascades to its owned children, then saves.
	/// </summary>
	public async Task SoftDeleteAsync(ISoftDeletable entity, Guid deletedBy)
	{
		var now = DateTime.UtcNow;
		MarkDeleted(entity, now, deletedBy);
		await CascadeSoftDeleteAsync(entity, now, deletedBy);
		await SaveChangesAsync();
	}

	private static void MarkDeleted(ISoftDeletable entity, DateTime when, Guid by)
	{
		entity.IsDeleted = true;
		entity.DeletedWhen = when;
		entity.DeletedBy = by;
	}

	private async Task CascadeSoftDeleteAsync(ISoftDeletable entity, DateTime when, Guid by)
	{
		switch (entity)
		{
			case Company company:
				await Entry(company).Collection(c => c.CompanySites).LoadAsync();
				await Entry(company).Collection(c => c.CompanyWorkflows).LoadAsync();
				await Entry(company).Collection(c => c.CompanyAddresses).LoadAsync();
				await Entry(company).Collection(c => c.CompanyPermits).LoadAsync();
				foreach (var s in company.CompanySites) { MarkDeleted(s, when, by); await CascadeSoftDeleteAsync(s, when, by); }
				foreach (var w in company.CompanyWorkflows) { MarkDeleted(w, when, by); await CascadeSoftDeleteAsync(w, when, by); }
				foreach (var a in company.CompanyAddresses) MarkDeleted(a, when, by);
				foreach (var p in company.CompanyPermits) { MarkDeleted(p, when, by); await CascadeSoftDeleteAsync(p, when, by); }
				break;

			case Site site:
				await Entry(site).Collection(s => s.Permits).LoadAsync();
				foreach (var p in site.Permits) { MarkDeleted(p, when, by); await CascadeSoftDeleteAsync(p, when, by); }
				break;

			case Permit permit:
				await Entry(permit).Collection(p => p.Attachments).LoadAsync();
				await Entry(permit).Collection(p => p.WorkflowHistories).LoadAsync();
				foreach (var a in permit.Attachments) MarkDeleted(a, when, by);
				foreach (var h in permit.WorkflowHistories) MarkDeleted(h, when, by);
				break;

			case Workflow workflow:
				await Entry(workflow).Collection(w => w.WorkflowSteps).LoadAsync();
				await Entry(workflow).Collection(w => w.WorkflowHistories).LoadAsync();
				foreach (var s in workflow.WorkflowSteps) MarkDeleted(s, when, by);
				foreach (var h in workflow.WorkflowHistories) MarkDeleted(h, when, by);
				break;

			case UserInfo user:
				await Entry(user).Collection(u => u.UserNotifications).LoadAsync();
				foreach (var n in user.UserNotifications) MarkDeleted(n, when, by);
				break;
		}
	}


	// -------------------------------------------------------------------------
	// Restore with cascade
	// -------------------------------------------------------------------------

	/// <summary>
	/// Restores a soft-deleted entity. Cascade children that were deleted
	/// within 10 seconds of the parent (i.e. as part of the same cascade)
	/// are also restored automatically.
	/// </summary>
	public async Task RestoreAsync(ISoftDeletable entity)
	{
		var deletedAt = entity.DeletedWhen;
		MarkRestored(entity);
		await CascadeRestoreAsync(entity, deletedAt);
		await SaveChangesAsync();
	}

	private static void MarkRestored(ISoftDeletable entity)
	{
		entity.IsDeleted = false;
		entity.DeletedWhen = null;
		entity.DeletedBy = null;
	}

	// Returns true when a child's DeletedWhen is within 10 s of the parent cascade timestamp.
	private static bool WasCascadeDeleted(ISoftDeletable child, DateTime? parentDeletedAt)
		 => child.IsDeleted &&
			 child.DeletedWhen.HasValue &&
			 parentDeletedAt.HasValue &&
			 Math.Abs((child.DeletedWhen.Value - parentDeletedAt.Value).TotalSeconds) <= 10;

	private async Task CascadeRestoreAsync(ISoftDeletable entity, DateTime? deletedAt)
	{
		switch (entity)
		{
			case Company company:
				var cSites = await Sites.IgnoreQueryFilters().Where(s => s.SiteCompany!.Id == company.Id).ToListAsync();
				var cWorkflows = await Workflows.IgnoreQueryFilters().Where(w => w.WorkflowCompany.Id == company.Id).ToListAsync();
				var cAddresses = await Addresses.IgnoreQueryFilters().Where(a => a.AddressCompany!.Id == company.Id).ToListAsync();
				var cPermits = await Permits.IgnoreQueryFilters().Where(p => p.Company!.Id == company.Id).ToListAsync();
				foreach (var s in cSites.Where(x => WasCascadeDeleted(x, deletedAt))) { MarkRestored(s); await CascadeRestoreAsync(s, deletedAt); }
				foreach (var w in cWorkflows.Where(x => WasCascadeDeleted(x, deletedAt))) { MarkRestored(w); await CascadeRestoreAsync(w, deletedAt); }
				foreach (var a in cAddresses.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(a);
				foreach (var p in cPermits.Where(x => WasCascadeDeleted(x, deletedAt))) { MarkRestored(p); await CascadeRestoreAsync(p, deletedAt); }
				break;

			case Site site:
				var sPermits = await Permits.IgnoreQueryFilters().Where(p => p.Site!.Id == site.Id).ToListAsync();
				foreach (var p in sPermits.Where(x => WasCascadeDeleted(x, deletedAt))) { MarkRestored(p); await CascadeRestoreAsync(p, deletedAt); }
				break;

			case Permit permit:
				var pAttachments = await Attachments.IgnoreQueryFilters().Where(a => a.Permit!.Id == permit.Id).ToListAsync();
				var pHistories = await WorkflowHistories.IgnoreQueryFilters().Where(h => h.Permit!.Id == permit.Id).ToListAsync();
				foreach (var a in pAttachments.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(a);
				foreach (var h in pHistories.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(h);
				break;

			case Workflow workflow:
				var wSteps = await WorkflowSteps.IgnoreQueryFilters().Where(s => s.WorkflowStepWorkflow!.Id == workflow.Id).ToListAsync();
				var wHistories = await WorkflowHistories.IgnoreQueryFilters().Where(h => h.HistoryWorkflow!.Id == workflow.Id).ToListAsync();
				foreach (var s in wSteps.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(s);
				foreach (var h in wHistories.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(h);
				break;

			case UserInfo user:
				var uNotifs = await Notifications.IgnoreQueryFilters().Where(n => n.NotificationUser!.Id == user.Id).ToListAsync();
				foreach (var n in uNotifs.Where(x => WasCascadeDeleted(x, deletedAt))) MarkRestored(n);
				break;
		}
	}
}