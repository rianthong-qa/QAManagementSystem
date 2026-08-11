using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.UnitTests;

public sealed class ProjectTests
{
    [Fact] public void Project_normalizes_code(){var project=new Project(" pmx2 ","ProMaxx2",null,null,null);Assert.Equal("PMX2",project.ProjectCode);Assert.True(project.IsActive);}
    [Fact] public void Project_requires_name()=>Assert.Throws<ArgumentException>(()=>new Project("PMX2"," ",null,null,null));
    [Fact] public void Deactivate_is_soft_delete(){var project=new Project("PMX2","ProMaxx2",null,null,null);project.Deactivate(Guid.NewGuid());Assert.False(project.IsActive);Assert.Equal("Inactive",project.Status);}
    [Fact] public void Module_cannot_be_its_own_parent(){var module=new ProductModule(Guid.NewGuid(),"CORE","Core",null,null,null,null);Assert.Throws<InvalidOperationException>(()=>module.Update("Core",module.ModuleId,null,null,null));}
}
