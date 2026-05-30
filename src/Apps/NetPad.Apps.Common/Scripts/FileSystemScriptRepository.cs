using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetPad.Configuration;
using NetPad.Data;
using NetPad.DotNet;
using NetPad.Exceptions;
using NetPad.Scripts;

namespace NetPad.Apps.Scripts;

/// <summary>
/// An implementation of <see cref="IScriptRepository"/> that persists scripts to the local file system.
/// </summary>
public class FileSystemScriptRepository : IScriptRepository
{
    private readonly Settings _settings;
    private readonly IScriptSerializerFactory _serializerFactory;
    private readonly IDataConnectionRepository _dataConnectionRepository;
    private readonly IDotNetInfo _dotNetInfo;
    private readonly ILogger<FileSystemScriptRepository> _logger;

    public FileSystemScriptRepository(
        Settings settings,
        IScriptSerializerFactory serializerFactory,
        IDataConnectionRepository dataConnectionRepository,
        IDotNetInfo dotNetInfo,
        ILogger<FileSystemScriptRepository> logger)
    {
        _settings = settings;
        _serializerFactory = serializerFactory;
        _dataConnectionRepository = dataConnectionRepository;
        _dotNetInfo = dotNetInfo;
        _logger = logger;
        Directory.CreateDirectory(GetRepositoryDirPath());
    }

    public async Task<IList<Script>> GetAllAsync()
    {
        var scripts = new ConcurrentBag<Script>();

        await Parallel.ForEachAsync(
            EnumerateScriptFiles(),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (scriptFile, _) =>
            {
                try
                {
                    var script = await GetAsync(scriptFile);
                    scripts.Add(script);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to load script: {FilePath}", scriptFile);
                }
            });

        return [.. scripts];
    }

    public Task<IList<ScriptSummary>> GetSummariesAsync()
    {
        var summaries = new List<ScriptSummary>();

        foreach (var scriptFile in EnumerateScriptFiles())
        {
            if (!_serializerFactory.GetForPath(scriptFile).TryReadSummary(scriptFile, out var summary))
                continue;

            summaries.Add(summary);
        }

        return Task.FromResult<IList<ScriptSummary>>(summaries);
    }

    public Task<Script> CreateAsync(string name, DotNetFrameworkVersion targetFrameworkVersion)
    {
        var script = new Script(
            ScriptIdGenerator.NewId(),
            name,
            new ScriptConfig(ScriptKind.Program, targetFrameworkVersion));

        return Task.FromResult(script);
    }

    public async Task<Script> GetAsync(string path)
    {
        if (!_serializerFactory.IsKnownScriptPath(path))
            throw new InvalidOperationException(
                $"Script file must end with one of: {string.Join(", ", _serializerFactory.GetAllFileExtensions())}");

        var fileInfo = new FileInfo(path);

        if (!fileInfo.Exists)
            throw new ScriptNotFoundException(path);

        return await DeserializeScriptAsync(path);
    }

    public async Task<Script?> GetAsync(Guid scriptId)
    {
        foreach (var scriptFile in EnumerateScriptFiles())
        {
            Guid? scriptIdFromFile = TryReadScriptId(scriptFile);

            if (scriptIdFromFile == null || scriptId != scriptIdFromFile)
                continue;

            return await GetAsync(scriptFile);
        }

        return null;
    }

    public async Task<IList<Script>> GetAsync(HashSet<Guid> scriptIds)
    {
        var scripts = new List<Script>();
        var remaining = new HashSet<Guid>(scriptIds);

        foreach (var scriptFile in EnumerateScriptFiles())
        {
            if (remaining.Count == 0)
                break;

            Guid? scriptIdFromFile = TryReadScriptId(scriptFile);

            if (scriptIdFromFile == null || !scriptIds.Contains(scriptIdFromFile.Value))
                continue;

            try
            {
                var script = await GetAsync(scriptFile);
                scripts.Add(script);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to load script: {FilePath}", scriptFile);
            }
        }

        return scripts;
    }

    public async Task<Script> SaveAsync(Script script)
    {
        if (script.Path == null)
            throw new InvalidOperationException($"{nameof(script.Path)} is not set. Cannot save script.");

        if (!_serializerFactory.IsKnownScriptPath(script.Path))
            throw new InvalidOperationException(
                $"Script file must end with one of: {string.Join(", ", _serializerFactory.GetAllFileExtensions())}");

        var content = _serializerFactory.GetForPath(script.Path).Serialize(script);
        await File.WriteAllTextAsync(script.Path, content).ConfigureAwait(false);

        script.IsDirty = false;
        return script;
    }

    public void Rename(Script script, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Name cannot be empty", nameof(newName));

        // Basic protection against malicious calls
        if (newName.Contains('/') || newName.Contains('\\'))
            throw new InvalidOperationException($"Script name must not contain a path separator");

        if (script.IsNew)
        {
            script.SetName(newName);
        }
        else
        {
            var oldName = script.Name;
            var oldPath = script.Path;

            script.SetName(newName);
            var newPath = script.Path;

            if (File.Exists(newPath))
            {
                script.SetName(oldName);
                throw new Exception(
                    $"A file already exists at path: {newPath}. Renaming script will overwrite that file");
            }

            File.Move(oldPath!, newPath!, overwrite: false);
        }
    }

    public Task DeleteAsync(Script script)
    {
        if (script.Path == null)
            throw new InvalidOperationException($"{nameof(script.Path)} is not set. Cannot delete script.");

        if (!_serializerFactory.IsKnownScriptPath(script.Path))
            throw new InvalidOperationException(
                $"Script file must end with one of: {string.Join(", ", _serializerFactory.GetAllFileExtensions())}");

        if (!File.Exists(script.Path))
            throw new InvalidOperationException($"{nameof(script.Path)} does not exist. Cannot delete script.");

        File.Delete(script.Path);
        return Task.CompletedTask;
    }

    private async Task<Script> DeserializeScriptAsync(string path)
    {
        var script = await _serializerFactory
            .GetForPath(path)
            .DeserializeAsync(path, _dataConnectionRepository, _dotNetInfo);
        script.SetPath(path);
        return script;
    }

    private string GetRepositoryDirPath() => _settings.ScriptsDirectoryPath;

    private IEnumerable<string> EnumerateScriptFiles()
    {
        var dir = GetRepositoryDirPath();
        return _serializerFactory.GetAllFileExtensions()
            .SelectMany(ext => Directory.EnumerateFiles(dir, $"*{ext}", SearchOption.AllDirectories))
            .Where(_serializerFactory.IsKnownScriptPath);
    }

    private Guid? TryReadScriptId(string path)
    {
        _serializerFactory.GetForPath(path).TryReadSummary(path, out var summary);
        return summary?.Id;
    }
}
