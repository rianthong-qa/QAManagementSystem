using ProMaxx2.QA.Domain.Identity;
namespace ProMaxx2.QA.UnitTests;
public sealed class UserAdministrationTests
{
 [Fact]public void User_can_be_deactivated_and_activated(){var user=new User("qa01","QA 01",null,"hash");user.Deactivate();Assert.False(user.IsActive);user.Activate();Assert.True(user.IsActive);}
 [Fact]public void User_update_requires_display_name(){var user=new User("qa01","QA 01",null,"hash");Assert.Throws<ArgumentException>(()=>user.Update(" ",null));}
 [Fact]public void Password_reset_replaces_hash(){var user=new User("qa01","QA 01",null,"old");user.ResetPassword("new");Assert.Equal("new",user.PasswordHash);}
}
