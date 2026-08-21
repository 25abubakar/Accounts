
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Accounts {
    public static class TestEF {
        public static void Run(ApplicationDbContext db) {
            try {
                var visiblePersonIds = new System.Collections.Generic.HashSet<Guid>(); // empty means no filter logic
                var sqlRows = db.AttendanceDeductionReportRows
                    .FromSqlRaw(
                        "EXEC dbo.usp_Attendance_DeductionReport @TenantId, @Year, @Month, @VisiblePersonIds",
                        new SqlParameter("@TenantId", 2007),
                        new SqlParameter("@Year", 2026),
                        new SqlParameter("@Month", 8),
                        new SqlParameter("@VisiblePersonIds", JsonSerializer.Serialize(visiblePersonIds)))
                    .AsNoTracking()
                    .ToList();
                Console.WriteLine("SUCCESS! Rows: " + sqlRows.Count);
            } catch (Exception ex) {
                Console.WriteLine("ERROR IN EF: " + ex.Message);
            }
        }
    }
}