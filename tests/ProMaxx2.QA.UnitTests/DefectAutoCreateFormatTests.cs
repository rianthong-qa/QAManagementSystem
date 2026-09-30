using System.Globalization;
using ProMaxx2.QA.Api.Services;
namespace ProMaxx2.QA.UnitTests;
public sealed class DefectAutoCreateFormatTests
{
 [Fact]public void Test_time_is_bangkok_time_with_buddhist_year_even_on_thai_culture_server()
 {
  var previous=CultureInfo.CurrentCulture;
  try
  {
   // เครื่อง server จริงตั้ง culture เป็น th-TH — ต้องไม่บวก 543 ซ้ำ
   CultureInfo.CurrentCulture=new CultureInfo("th-TH");
   Assert.Equal("28/08/2569 11:02 น.",DefectAutoCreateService.FormatThaiTime(new DateTime(2026,8,28,4,2,0,DateTimeKind.Utc)));
   // ข้ามวันตามเวลาไทย
   Assert.Equal("01/10/2569 06:30 น.",DefectAutoCreateService.FormatThaiTime(new DateTime(2026,9,30,23,30,0,DateTimeKind.Utc)));
  }
  finally{CultureInfo.CurrentCulture=previous;}
 }
}
