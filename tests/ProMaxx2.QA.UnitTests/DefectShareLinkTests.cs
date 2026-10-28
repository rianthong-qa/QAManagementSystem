using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Infrastructure.Persistence;
namespace ProMaxx2.QA.UnitTests;
public sealed class DefectShareLinkTests
{
 private sealed class FakeEnv(string root):IWebHostEnvironment{public string ApplicationName{get;set;}="Tests";public IFileProvider ContentRootFileProvider{get;set;}=null!;public string ContentRootPath{get;set;}=root;public string EnvironmentName{get;set;}="Test";public IFileProvider WebRootFileProvider{get;set;}=null!;public string WebRootPath{get;set;}=root;}
 private static QaDbContext NewDb()=>new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
 private static DefectShareLinkService NewService(QaDbContext db,string? baseUrl=null,IDataProtectionProvider? dp=null)=>new(db,dp??new EphemeralDataProtectionProvider(),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["PublicWebBaseUrl"]=baseUrl}).Build());
 private static SharedDefectsController NewController(QaDbContext db,DefectShareLinkService s,string root)=>new(db,s,new DefectActivityService(db),new DefectImageStorage(new FakeEnv(root))){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
 private static readonly byte[] Png=[137,80,78,71,13,10,26,10,0,0];
 private static IFormFile PngFile(string name)=>new FormFile(new MemoryStream(Png),0,Png.Length,"files",name);
 [Fact]public async Task Comment_with_text_and_images_is_listed_with_author_and_kept_out_of_defect_images()
 {
  using var db=NewDb();var root=Path.Combine(Path.GetTempPath(),"defect-comment-"+Guid.NewGuid().ToString("N"));var storage=new DefectImageStorage(new FakeEnv(root));var svc=new DefectActivityService(db);
  var author=new ProMaxx2.QA.Domain.Identity.User("qa1","QA One",null,"hash");db.Users.Add(author);
  var d=new Defect(Guid.NewGuid(),null,null,null,"PMX2-DEF-002","t","High","Open",null,null,null,null,null,null);db.Defects.Add(d);
  db.DefectActivities.Add(new DefectActivity(d.DefectId,"CrmComment","CRM Ticket #BHD1 — 5640: แก้แล้ว รอทดสอบ",null));await db.SaveChangesAsync();
  try
  {
   var (images,error)=await storage.ReadAsync([PngFile("a.png"),PngFile("b.png")],[],default);Assert.Null(error);
   var commentId=await svc.AddCommentWithImagesAsync(d.DefectId," ดูรูปนี้ ",images,storage,author.UserId,default);
   await svc.AddCommentWithImagesAsync(d.DefectId,null,(await storage.ReadAsync([PngFile("c.png")],[],default)).Images,storage,author.UserId,default);
   await Assert.ThrowsAsync<ArgumentException>(()=>svc.AddCommentWithImagesAsync(d.DefectId,"  ",[],storage,author.UserId,default));
   var comments=await svc.GetCommentsAsync(d.DefectId,default);
   Assert.Equal(3,comments.Count);
   Assert.Equal(("Crm","CRM · 5640","แก้แล้ว รอทดสอบ"),(comments[0].Source,comments[0].AuthorName,comments[0].Body));
   var mine=comments.Single(x=>x.CommentId==commentId);Assert.Equal("ดูรูปนี้",mine.Body);Assert.Equal("QA One",mine.AuthorName);Assert.Equal(2,mine.Attachments.Count);
   Assert.True(File.Exists(storage.PathOf(d.DefectId,db.DefectAttachments.First(x=>x.CommentId==commentId).StoredFileName)));
   var s=NewService(db);var code=await s.GetOrCreateCodeAsync(d.DefectId,default);var c=NewController(db,s,root);
   var dto=Assert.IsType<SharedDefectDto>(Assert.IsType<OkObjectResult>((await c.Get(code,default)).Result).Value);
   Assert.Empty(dto.Attachments);Assert.Equal(3,dto.Comments.Count);Assert.All(dto.Comments,x=>Assert.Null(x.AuthorUserId));
   Assert.IsType<PhysicalFileResult>(await c.Attachment(code,mine.Attachments[0].AttachmentId,default));
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [Fact]public async Task Image_rules_reject_more_than_five_or_non_images(){var storage=new DefectImageStorage(new FakeEnv(Path.GetTempPath()));Assert.NotNull((await storage.ReadAsync(Enumerable.Range(0,6).Select(i=>PngFile($"{i}.png")).ToList(),[],default)).Error);Assert.NotNull((await storage.ReadAsync([new FormFile(new MemoryStream([1,2,3]),0,3,"files","x.png")],[],default)).Error);Assert.NotNull((await storage.ReadAsync([PngFile("x.gif")],[],default)).Error);}
 [Fact]public async Task Short_code_is_8_chars_reused_per_defect_and_resolves_back()
 {
  using var db=NewDb();var s=NewService(db,"https://example.test/");var id=Guid.NewGuid();
  var url=await s.GetOrCreateUrlAsync(id,default);var code=await s.GetOrCreateCodeAsync(id,default);
  Assert.Equal($"https://example.test/?d={code}",url);Assert.Equal(8,code.Length);Assert.Single(db.DefectShareLinks);
  Assert.Equal(id,await s.ResolveAsync(code,default));
  Assert.NotEqual(code,await s.GetOrCreateCodeAsync(Guid.NewGuid(),default));
  Assert.Null(await s.ResolveAsync("zzzzzzzz",default));Assert.Null(await s.ResolveAsync("",default));
 }
 [Fact]public async Task Legacy_long_token_still_resolves_but_tampered_or_foreign_tokens_do_not()
 {
  using var db=NewDb();var dp=new EphemeralDataProtectionProvider();var s=NewService(db,dp:dp);var id=Guid.NewGuid();var token=s.CreateLegacyToken(id);
  Assert.Equal(id,await s.ResolveAsync(token,default));
  var mid=token.Length/2;var tampered=token[..mid]+(token[mid]=='A'?'B':'A')+token[(mid+1)..];
  Assert.Null(await s.ResolveAsync(tampered,default));
  Assert.Null(await NewService(db).ResolveAsync(token,default));
  Assert.Null(await s.ResolveAsync(Guid.NewGuid().ToString(),default));
 }
 [Fact]public async Task Shared_endpoint_returns_defect_and_image_for_valid_code_only()
 {
  using var db=NewDb();var s=NewService(db);var root=Path.Combine(Path.GetTempPath(),"defect-share-"+Guid.NewGuid().ToString("N"));
  var d=new Defect(Guid.NewGuid(),null,null,null,"PMX2-DEF-001","ล้มเหลว","High","Open",null,"desc","steps","exp","act",null);db.Defects.Add(d);
  var attachmentId=Guid.NewGuid();var stored=$"{attachmentId:N}_shot.png";db.DefectAttachments.Add(new DefectAttachment(attachmentId,d.DefectId,"shot.png",stored,8,"image/png",null,DateTime.UtcNow));await db.SaveChangesAsync();
  var dir=Path.Combine(root,"App_Data","DefectAttachments",d.DefectId.ToString("N"));Directory.CreateDirectory(dir);await File.WriteAllBytesAsync(Path.Combine(dir,stored),new byte[]{137,80,78,71,13,10,26,10});
  try
  {
   var c=NewController(db,s,root);var code=await s.GetOrCreateCodeAsync(d.DefectId,default);
   var ok=Assert.IsType<OkObjectResult>((await c.Get(code,default)).Result);var dto=Assert.IsType<SharedDefectDto>(ok.Value);
   Assert.Equal("PMX2-DEF-001",dto.DefectCode);Assert.Equal("steps",dto.StepsToReproduce);Assert.Single(dto.Attachments);
   Assert.IsType<PhysicalFileResult>(await c.Attachment(code,attachmentId,default));
   Assert.IsType<NotFoundResult>(await c.Attachment(code,Guid.NewGuid(),default));
   Assert.IsType<NotFoundResult>((await c.Get("bogus",default)).Result);
   Assert.IsType<NotFoundResult>(await c.Attachment("bogus",attachmentId,default));
   d.SoftDelete(null);await db.SaveChangesAsync();
   Assert.IsType<NotFoundResult>((await c.Get(code,default)).Result);
   Assert.IsType<NotFoundResult>(await c.Attachment(code,attachmentId,default));
  }
  finally{Directory.Delete(root,true);}
 }
 [Fact]public void Emails_show_share_button_below_crm_button_only_when_link_given()
 {
  const string share="https://promaxx2.qahub.store/?d=k7m2x9qp";
  var assigned=EmailTemplates.DefectAssignedViaCrm("DEF-1","t","High","Open","P",null,null,null,null,null,"Dev","5640","BHD1","https://crm/x",share);
  var returned=EmailTemplates.CrmReturnedToOwner("DEF-1","t","High","Open","P",null,"Continue","BHD1","https://crm/x",share);
  foreach(var html in new[]{assigned,returned})
  {
   Assert.Contains($"href=\"{share}\"",html);Assert.Contains("ดูรายละเอียด Defect และรูปภาพ",html);
   Assert.True(html.IndexOf("เปิด Ticket ใน CRM",StringComparison.Ordinal)<html.IndexOf("ดูรายละเอียด Defect และรูปภาพ",StringComparison.Ordinal));
  }
  Assert.DoesNotContain("ดูรายละเอียด Defect และรูปภาพ",EmailTemplates.DefectAssignedViaCrm("DEF-1","t","High","Open","P",null,null,null,null,null,"Dev","5640","BHD1","https://crm/x"));
  Assert.Contains("href=\"https://x/?d=a&amp;b\"",EmailTemplates.ShareButton("https://x/?d=a&b"));
 }
 [Fact]public void Short_description_keeps_full_text_and_appends_link(){var d=CrmSendToCrmService.BuildCrmDescription("สั้น","https://x/?d=abc");Assert.StartsWith("สั้น",d);Assert.EndsWith("https://x/?d=abc",d);Assert.DoesNotContain("ตัดข้อความ",d);}
 [Fact]public void Long_description_is_cut_but_link_survives_within_crm_limit(){var url="https://promaxx2.qahub.store/?d=Ab3kZ9xQ";var d=CrmSendToCrmService.BuildCrmDescription(new string('ก',2000),url);Assert.Equal(1000,d.Length);Assert.EndsWith(url,d);Assert.Contains("[...ตัดข้อความ]",d);}
}
