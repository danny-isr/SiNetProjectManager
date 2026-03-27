using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VSDiagnostics;

namespace SiNetSQL.Benchmarks
{
    [CPUUsageDiagnoser]
    public class EmailVmConstructorBenchmark
    {
        private SiNetSQLDbContext _dbContext = null!;
        [GlobalSetup]
        public void Setup()
        {
            _dbContext = new SiNetSQLDbContext();
        }

        /// <summary>
        /// Simulates the synchronous DB-loading portion of EmailManagementViewModel.ctor().
        /// This is the code that blocks the UI thread for ~10% of total CPU.
        /// </summary>
        [Benchmark]
        public void LoadEmailVmData_Sync()
        {
            var projects = _dbContext.Projects.Include(p => p.Place).Include(p => p.Company).Include(p => p.ProjectStatus).Include(p => p.TypeOfProjectInProjects).Include(p => p.ProjectAssignments).OrderByDescending(p => p.Number).ToList();
            var places = _dbContext.Places.Where(p => p.InUse == true).OrderBy(p => p.Title).ToList();
            var companies = _dbContext.Companies.OrderBy(c => c.Title).ToList();
            var jobTypes = _dbContext.JobTypes.OrderBy(j => j.Title).ToList();
            var statuses = _dbContext.ProjectStatuses.OrderBy(s => s.Title).ToList();
            var users = _dbContext.Siusers.Where(u => u.IsActive).OrderBy(u => u.Name).ToList();
        }

        /// <summary>
        /// Optimized version: runs all 6 queries concurrently using Task.WhenAll,
        /// then collects results. This is what the async InitializeAsync pattern would do.
        /// </summary>
        [Benchmark]
        public async Task LoadEmailVmData_Async()
        {
            var projectsTask = _dbContext.Projects.Include(p => p.Place).Include(p => p.Company).Include(p => p.ProjectStatus).Include(p => p.TypeOfProjectInProjects).Include(p => p.ProjectAssignments).OrderByDescending(p => p.Number).ToListAsync();
            var placesTask = _dbContext.Places.Where(p => p.InUse == true).OrderBy(p => p.Title).ToListAsync();
            var companiesTask = _dbContext.Companies.OrderBy(c => c.Title).ToListAsync();
            var jobTypesTask = _dbContext.JobTypes.OrderBy(j => j.Title).ToListAsync();
            var statusesTask = _dbContext.ProjectStatuses.OrderBy(s => s.Title).ToListAsync();
            var usersTask = _dbContext.Siusers.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();
            await Task.WhenAll(projectsTask, placesTask, companiesTask, jobTypesTask, statusesTask, usersTask);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _dbContext?.Dispose();
        }
    }
}