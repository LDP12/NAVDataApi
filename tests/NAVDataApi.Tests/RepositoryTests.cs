using Microsoft.AspNetCore.Mvc;
using NAVDataApi.Controllers;
using NAVDataApi.Data;
using NAVDataApi.Models;

namespace NAVDataApi.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public void Create_allocates_distinct_ids_without_replacing_records()
    {
        var repository = new InMemoryNavRepository();
        var before = repository.GetAll();
        var first = repository.Create(Requests.Valid()).Record!;
        var second = repository.Create(Requests.Valid(2)).Record!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(before.Count + 2, repository.GetAll().Count);
        Assert.Equal(first, repository.GetById(first.Id));
        foreach (var record in before) Assert.Equal(record, repository.GetById(record.Id));
    }

    [Fact]
    public void Create_location_uses_the_existing_read_route()
    {
        var controller = new NavsController(new InMemoryNavRepository());
        var result = Assert.IsType<CreatedResult>(controller.Create(Requests.Valid()).Result);
        var record = Assert.IsType<NavRecord>(result.Value);
        Assert.Equal($"/api/navs/{record.Id}", result.Location);
    }

    [Fact]
    public void Create_preserves_full_decimal_precision()
    {
        var repository = new InMemoryNavRepository();
        var request = Requests.Valid();
        request.NavPerShare = 4.582123456789m;
        var record = repository.Create(request).Record!;
        Assert.Equal(request.NavPerShare, record.NavPerShare);
        Assert.Equal(Math.Round(request.NavPerShare * request.SharesOutstanding, 2), record.TotalNetAssets);
    }

    [Fact]
    public void Update_unknown_id_returns_not_found_without_mutation()
    {
        var repository = new InMemoryNavRepository();
        var before = repository.GetAll();
        Assert.Equal(RepositoryStatus.NotFound, repository.Update(int.MaxValue, Requests.Valid()).Status);
        Assert.Equal(before, repository.GetAll());
    }

    [Fact]
    public void Update_unchanged_identity_succeeds_and_persists()
    {
        var repository = new InMemoryNavRepository();
        var old = repository.GetAll()[0];
        var request = Requests.From(old);
        request.NavPerShare = 123.4567m;
        var result = repository.Update(old.Id, request);
        Assert.Equal(RepositoryStatus.Success, result.Status);
        Assert.Equal(123.4567m, repository.GetById(old.Id)!.NavPerShare);
    }

    [Fact]
    public void Update_replaces_every_field_in_storage()
    {
        var repository = new InMemoryNavRepository();
        var old = repository.GetAll()[0];
        var request = Requests.Valid();
        request.NavPerShare = 123m;
        var result = repository.Update(old.Id, request);
        Assert.Equal(RepositoryStatus.Success, result.Status);
        Assert.Equal(request.ToRecord(old.Id), repository.GetById(old.Id));
    }

    [Fact]
    public void Duplicate_operations_leave_all_records_unchanged()
    {
        var repository = new InMemoryNavRepository();
        var before = repository.GetAll();
        var duplicate = Requests.From(before[0]);
        Assert.Equal(RepositoryStatus.Conflict, repository.Create(duplicate).Status);
        Assert.Equal(RepositoryStatus.Conflict, repository.Update(before[1].Id, duplicate).Status);
        Assert.Equal(before, repository.GetAll());
    }

    [Fact]
    public async Task Concurrent_creates_allocate_unique_ids()
    {
        var repository = new InMemoryNavRepository();
        var before = repository.GetAll();
        var results = await Task.WhenAll(Enumerable.Range(1, 100).Select(day =>
            Task.Run(() => repository.Create(Requests.Valid(day)))));
        Assert.All(results, r => Assert.Equal(RepositoryStatus.Success, r.Status));
        Assert.Equal(100, results.Select(r => r.Record!.Id).Distinct().Count());
        Assert.Equal(before.Count + 100, repository.GetAll().Count);
        foreach (var record in before) Assert.Equal(record, repository.GetById(record.Id));
    }

    [Fact]
    public async Task Concurrent_duplicate_creates_have_one_winner()
    {
        var repository = new InMemoryNavRepository();
        var before = repository.GetAll().Count;
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() => repository.Create(Requests.Valid())))) ;
        Assert.Single(results, r => r.Status == RepositoryStatus.Success);
        Assert.Equal(49, results.Count(r => r.Status == RepositoryStatus.Conflict));
        Assert.Equal(before + 1, repository.GetAll().Count);
    }

    [Fact]
    public async Task Concurrent_updates_to_same_identity_have_one_winner()
    {
        var repository = new InMemoryNavRepository();
        var records = repository.GetAll();
        var results = await Task.WhenAll(records.Select(r =>
            Task.Run(() => repository.Update(r.Id, Requests.Valid()))));
        Assert.Single(results, r => r.Status == RepositoryStatus.Success);
        Assert.Equal(records.Count - 1, results.Count(r => r.Status == RepositoryStatus.Conflict));
        Assert.Single(repository.GetAll(), r => r.Isin == Requests.Valid().Isin);
    }
}
