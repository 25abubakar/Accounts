                                                                                                                                                                                                                                                                
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    CREATE   PROCEDURE dbo.usp_Attendance_DailyReport
        @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
    AS
    BEGIN
        SET NOCOUNT ON;
        DECLARE @ProcessId int,@DayOff int,@Holiday int,@PlatformDayOff int

(1 rows affected)
