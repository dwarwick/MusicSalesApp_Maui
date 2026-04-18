using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class PersonaViewModelTests
{
    [Test]
    public void PersonaName_SetAndGet()
    {
        var vm = new PersonaViewModel { PersonaName = "Test Artist" };
        Assert.That(vm.PersonaName, Is.EqualTo("Test Artist"));
    }

    [Test]
    public void PersonaImageUrl_SetAndGet()
    {
        var vm = new PersonaViewModel { PersonaImageUrl = "https://img.test/pic.jpg" };
        Assert.That(vm.PersonaImageUrl, Is.EqualTo("https://img.test/pic.jpg"));
    }

    [Test]
    public void PersonaBio_SetAndGet()
    {
        var vm = new PersonaViewModel { PersonaBio = "A long bio text." };
        Assert.That(vm.PersonaBio, Is.EqualTo("A long bio text."));
    }

    [Test]
    public void PersonaImageUrl_CanBeNull()
    {
        var vm = new PersonaViewModel { PersonaImageUrl = null };
        Assert.That(vm.PersonaImageUrl, Is.Null);
    }

    [Test]
    public void HasImage_TrueWhenImageUrlSet()
    {
        var vm = new PersonaViewModel { PersonaImageUrl = "https://img.test/pic.jpg" };
        Assert.That(vm.HasImage, Is.True);
    }

    [Test]
    public void HasImage_FalseWhenImageUrlNull()
    {
        var vm = new PersonaViewModel { PersonaImageUrl = null };
        Assert.That(vm.HasImage, Is.False);
    }

    [Test]
    public void HasImage_FalseWhenImageUrlEmpty()
    {
        var vm = new PersonaViewModel { PersonaImageUrl = "" };
        Assert.That(vm.HasImage, Is.False);
    }

    [Test]
    public void HasBio_TrueWhenBioSet()
    {
        var vm = new PersonaViewModel { PersonaBio = "Some bio" };
        Assert.That(vm.HasBio, Is.True);
    }

    [Test]
    public void HasBio_FalseWhenBioEmpty()
    {
        var vm = new PersonaViewModel { PersonaBio = "" };
        Assert.That(vm.HasBio, Is.False);
    }

    [Test]
    public void HasBio_FalseWhenBioNull()
    {
        var vm = new PersonaViewModel { PersonaBio = null! };
        Assert.That(vm.HasBio, Is.False);
    }

    [Test]
    public void PropertyChanged_RaisedForPersonaName()
    {
        var vm = new PersonaViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonaViewModel.PersonaName))
                raised = true;
        };

        vm.PersonaName = "New Name";

        Assert.That(raised, Is.True);
    }

    [Test]
    public void PropertyChanged_RaisedForPersonaBio()
    {
        var vm = new PersonaViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonaViewModel.PersonaBio))
                raised = true;
        };

        vm.PersonaBio = "New bio content";

        Assert.That(raised, Is.True);
    }

    [Test]
    public void HasImage_PropertyChanged_RaisedWhenImageUrlChanges()
    {
        var vm = new PersonaViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonaViewModel.HasImage))
                raised = true;
        };

        vm.PersonaImageUrl = "https://img.test/pic.jpg";

        Assert.That(raised, Is.True);
    }

    [Test]
    public void HasBio_PropertyChanged_RaisedWhenBioChanges()
    {
        var vm = new PersonaViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PersonaViewModel.HasBio))
                raised = true;
        };

        vm.PersonaBio = "New bio";

        Assert.That(raised, Is.True);
    }
}
