-- Shared read-only salary projection for payroll Preview and Create.
CREATE OR ALTER FUNCTION dbo.fn_Payroll_ScaleProjection
(
    @TenantId int, @ScaleId int, @Anchor date, @AsOf date,
    @ProfileBasic decimal(18,2), @ProfileIncrement decimal(18,2), @ProfileMax decimal(18,2)
)
RETURNS @Projection TABLE
(
    SalaryScaleId int NULL, ScaleBasic decimal(18,2), YearlyIncrement decimal(18,2),
    MaxSalary decimal(18,2), CurrentPay decimal(18,2), EffectiveScaleDate date NULL
)
AS
BEGIN
    DECLARE @Basic decimal(18,2)=COALESCE(@ProfileBasic,0),
        @Inc decimal(18,2)=COALESCE(@ProfileIncrement,0), @Max decimal(18,2)=COALESCE(@ProfileMax,0),
        @ApplyAfter int=0, @Months nvarchar(100), @Name nvarchar(80), @Years int=0,
        @Consumed int=0, @Pay decimal(18,2), @Guard int=0;
    SELECT @Basic=BasicSalary,@Inc=YearlyIncrement,@Max=MaximumSalary,
        @ApplyAfter=CASE WHEN ApplyAfter>0 THEN ApplyAfter ELSE 0 END,
        @Months=IncrementMonths,@Name=ScaleName
    FROM dbo.SalaryScales WHERE TenantId=@TenantId AND Id=@ScaleId AND IsActive=1;
    IF @Name IS NULL SET @ScaleId=NULL;
    IF @Anchor IS NOT NULL AND @Anchor<=@AsOf
    BEGIN
        SET @Years=DATEDIFF(year,@Anchor,@AsOf);
        IF DATEADD(year,@Years,@Anchor)>@AsOf SET @Years-=1;
        SET @Years=CASE WHEN @Years>@ApplyAfter THEN @Years-@ApplyAfter ELSE 0 END;
    END;

    DECLARE @InstallmentMonths TABLE(MonthNo int PRIMARY KEY);
    INSERT @InstallmentMonths SELECT DISTINCT TRY_CONVERT(int,value)
    FROM STRING_SPLIT(COALESCE(@Months,N''),N',') WHERE TRY_CONVERT(int,value) BETWEEN 1 AND 12;
    IF EXISTS(SELECT 1 FROM @InstallmentMonths)
    BEGIN
        -- Preserve explicitly configured calendar installment schedules (no scale ladder).
        DECLARE @Eligible date=DATEADD(year,@ApplyAfter,@Anchor), @Year int,
            @Count int=(SELECT COUNT(*) FROM @InstallmentMonths), @Earned int=0;
        SET @Year=YEAR(@Eligible);
        WHILE @Year<=YEAR(@AsOf) AND @Eligible<=@AsOf
        BEGIN
            SELECT @Earned+=COUNT(*) FROM @InstallmentMonths m
            CROSS APPLY(SELECT DATEFROMPARTS(@Year,m.MonthNo,
                CASE WHEN DAY(@Eligible)>DAY(EOMONTH(DATEFROMPARTS(@Year,m.MonthNo,1)))
                THEN DAY(EOMONTH(DATEFROMPARTS(@Year,m.MonthNo,1))) ELSE DAY(@Eligible) END) d) x
            WHERE x.d>=@Eligible AND x.d<=@AsOf;
            SET @Year+=1;
        END;
        SET @Pay=CASE WHEN @Basic<0 THEN 0 ELSE @Basic END
            + @Earned*ROUND(CASE WHEN @Inc<0 THEN 0 ELSE @Inc END/@Count,2);
    END
    ELSE
    BEGIN
        DECLARE @Prefix nvarchar(80)=@Name, @Dash int=CHARINDEX(N'-',REVERSE(@Name)),
            @Steps int, @Next int, @Position int;
        IF @Dash>0 AND TRY_CONVERT(int,RIGHT(@Name,@Dash-1)) IS NOT NULL
            SET @Prefix=LEFT(@Name,LEN(@Name)-@Dash);
        DECLARE @Ladder TABLE(Position int,Id int);
        INSERT @Ladder
        SELECT ROW_NUMBER() OVER(ORDER BY CASE WHEN DisplayOrder=0 THEN 2147483647 ELSE DisplayOrder END,
            COALESCE(TRY_CONVERT(int,RIGHT(ScaleName,CHARINDEX(N'-',REVERSE(ScaleName)+N'-')-1)),0),ScaleName),Id
        FROM dbo.SalaryScales s WHERE TenantId=@TenantId AND IsActive=1
          AND CASE WHEN CHARINDEX(N'-',REVERSE(ScaleName))>0
                AND TRY_CONVERT(int,RIGHT(ScaleName,CHARINDEX(N'-',REVERSE(ScaleName)+N'-')-1)) IS NOT NULL
              THEN LEFT(ScaleName,LEN(ScaleName)-CHARINDEX(N'-',REVERSE(ScaleName))) ELSE ScaleName END=@Prefix
          AND Id=(SELECT MIN(s2.Id) FROM dbo.SalaryScales s2 WHERE s2.TenantId=@TenantId
              AND s2.IsActive=1 AND s2.ScaleName=s.ScaleName);
        WHILE @Guard<32
        BEGIN
            SET @Guard+=1;
            SET @Pay=@Basic+@Years*@Inc;
            IF @Inc<=0 OR @Years<=0 BREAK;
            SET @Steps=CASE WHEN @Max<=0 OR @Max<=@Basic THEN 536870911 ELSE FLOOR((@Max-@Basic)/@Inc) END;
            IF @Years<=@Steps BREAK;
            SET @Position=NULL; SET @Next=NULL;
            SELECT @Position=Position FROM @Ladder WHERE Id=@ScaleId;
            SELECT @Next=Id FROM @Ladder WHERE Position=@Position+1;
            IF @Next IS NULL BREAK;
            SET @Years-=@Steps; SET @Consumed+=@Steps; SET @ScaleId=@Next;
            SELECT @Basic=BasicSalary,@Inc=YearlyIncrement,@Max=MaximumSalary
            FROM dbo.SalaryScales WHERE TenantId=@TenantId AND Id=@ScaleId;
        END;
    END;
    IF @Max>0 AND @Pay>@Max SET @Pay=@Max;
    INSERT @Projection VALUES(@ScaleId,@Basic,@Inc,@Max,ROUND(@Pay,2),
        CASE WHEN @Consumed>0 THEN DATEADD(year,@Consumed,@Anchor) END);
    RETURN;
END;
