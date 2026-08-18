namespace ProMaxx2.QA.Domain.TestManagement;

public sealed class TestSuite
{
    private TestSuite() { }
    public TestSuite(Guid projectId,string code,string name,string?type,string?description,string?riskTier)
    {
        if(projectId==Guid.Empty)throw new ArgumentException("Project is required.");
        Validate(code,name);
        TestSuiteId=Guid.NewGuid();ProjectId=projectId;SuiteCode=code.Trim().ToUpperInvariant();SuiteName=name.Trim();SuiteType=type?.Trim();Description=description?.Trim();RiskTier=riskTier?.Trim();IsActive=true;
    }
    public Guid TestSuiteId{get;private set;} public Guid ProjectId{get;private set;} public string SuiteCode{get;private set;}=string.Empty; public string SuiteName{get;private set;}=string.Empty; public string?SuiteType{get;private set;} public string?Description{get;private set;} public string?RiskTier{get;private set;} public bool IsActive{get;private set;} public ICollection<TestSuiteCase>Cases{get;private set;}=[];
    public void Update(string name,string?type,string?description,string?riskTier,bool isActive){Validate(SuiteCode,name);SuiteName=name.Trim();SuiteType=type?.Trim();Description=description?.Trim();RiskTier=riskTier?.Trim();IsActive=isActive;}
    public void Deactivate(){IsActive=false;}
    private static void Validate(string code,string name){if(string.IsNullOrWhiteSpace(code))throw new ArgumentException("Suite code is required.");if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Suite name is required.");}
}

public sealed class TestSuiteCase
{
    private TestSuiteCase() { }
    public TestSuiteCase(Guid suiteId,Guid testCaseId,int sortOrder,bool isRequired){TestSuiteId=suiteId;TestCaseId=testCaseId;SortOrder=sortOrder;IsRequired=isRequired;}
    public Guid TestSuiteId{get;private set;} public Guid TestCaseId{get;private set;} public int SortOrder{get;private set;} public bool IsRequired{get;private set;} public TestSuite Suite{get;private set;}=null!; public TestCase TestCase{get;private set;}=null!;
    public void Update(int sortOrder,bool isRequired){if(sortOrder<1)throw new ArgumentOutOfRangeException(nameof(sortOrder));SortOrder=sortOrder;IsRequired=isRequired;}
}
