using System.Collections.Specialized;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

public class ObservableRangeCollectionTests
{
    [Test]
    public void ReplaceAll_ReplacesContentsWithSingleResetNotification()
    {
        var collection = new ObservableRangeCollection<int>([1, 2]);
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => notifications.Add(args);

        collection.ReplaceAll([3, 4, 5]);

        Assert.Multiple(() =>
        {
            Assert.That(collection, Is.EqualTo(new[] { 3, 4, 5 }));
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Reset));
        });
    }

    [Test]
    public void ReplaceAll_CanReplaceFromTheSameCollection()
    {
        var collection = new ObservableRangeCollection<int>([1, 2, 3]);

        collection.ReplaceAll(collection);

        Assert.That(collection, Is.EqualTo(new[] { 1, 2, 3 }));
    }
}
