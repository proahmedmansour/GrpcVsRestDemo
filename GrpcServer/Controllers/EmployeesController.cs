using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrpcServer.Data;
using GrpcServer.Models;
using GrpcServer.Extensions;
using System.Diagnostics;

namespace GrpcServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EmployeesController> _logger;

        public EmployeesController(ApplicationDbContext context, ILogger<EmployeesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployees([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var query = _context.Employees
                    .AsNoTracking()
                    .Select(e => new
                    {
                        e.Id,
                        e.FirstName,
                        e.LastName,
                        e.Email,
                        e.Department,
                        e.Salary,
                        e.DateOfBirth
                    });

                var totalCount = await query.CountAsync();
                var employees = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                stopwatch.Stop();
                _logger.LogInformation($"REST API: Retrieved {employees.Count} employees in {stopwatch.ElapsedMilliseconds}ms");

                return Ok(new
                {
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                    Data = employees
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving employees");
                return StatusCode(500, "An error occurred while retrieving employees");
            }
        }

        [HttpGet("stream")]
        public async Task GetEmployeesStream()
        {
            Response.Headers.Add("Content-Type", "text/event-stream");
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            var stopwatch = Stopwatch.StartNew();
            var batchSize = 1000;
            var totalProcessed = 0;

            try
            {
                var query = _context.Employees
                    .AsNoTracking()
                    .Select(e => new
                    {
                        e.Id,
                        e.FirstName,
                        e.LastName,
                        e.Email,
                        e.Department,
                        e.Salary,
                        e.DateOfBirth
                    });

                await foreach (var batch in query.AsAsyncEnumerable().Buffer(batchSize))
                {
                    foreach (var employee in batch)
                    {
                        await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(employee)}\n\n");
                        await Response.Body.FlushAsync();
                    }

                    totalProcessed += batch.Count;
                    _logger.LogInformation($"REST API Stream: Processed {totalProcessed} employees");
                }

                stopwatch.Stop();
                _logger.LogInformation($"REST API Stream: Completed in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming employees");
                await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new { error = "An error occurred while streaming employees" })}\n\n");
            }
            finally
            {
                await Response.Body.FlushAsync();
            }
        }
    }
} 