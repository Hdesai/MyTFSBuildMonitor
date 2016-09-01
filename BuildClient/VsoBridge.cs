using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BuildClient.Configuration;
using Newtonsoft.Json;

namespace BuildClient
{
    public class VsoBridge: IBuildEventSystem, IDisposable
    {
        private readonly IBuildConfigurationManager _buildConfigurationManager;
        private static readonly string Version = "2.0";
        private readonly HttpClient _client;
        private readonly ResponseList<ProjectDto> _projects;
        private readonly List<Tuple<Guid,int, BuildData>> _builds;

        public VsoBridge(IBuildConfigurationManager buildConfigurationManager)
        {
            _buildConfigurationManager = buildConfigurationManager;


            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_buildConfigurationManager.TfsAccountPassword}"));
            var projectsToMonitor = _buildConfigurationManager
                .BuildMappers.ToArray();

            _client = new HttpClient
            {
                BaseAddress = new Uri(_buildConfigurationManager.TeamFoundationUrl)
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //connect to the REST endpoint            
            var response = _client.GetAsync("_apis/projects?stateFilter=All&api-version=1.0").Result;


            _builds = new List<Tuple<Guid,int, BuildData>>();
            //check to see if we have a succesfull respond
            if (!response.IsSuccessStatusCode) return;
            
            //set the viewmodel from the content in the response
            var value = response.Content.ReadAsStringAsync().Result;
            _projects = JsonConvert.DeserializeObject<ResponseList<ProjectDto>>(value);

            var query = from element in projectsToMonitor
                from proj in _projects.Value.Where(p => p.Name == element.TfsProjectToMonitor)
                select new
                {
                    proj.Id,
                    BuildName = element.TfsBuildToMonitor
                };

            foreach (var source in query)
            {
                var defnUrl =
                    $"{_buildConfigurationManager.TeamFoundationUrl}/DefaultCollection/{source.Id}/_apis/build/definitions?api-version={Version}&query=Name eq '{source.BuildName}'&statusFilter=completed&$top=1";

                var defnResponse = _client.GetAsync(defnUrl).Result;
                if (!defnResponse.IsSuccessStatusCode) return;

                var buildDefn = defnResponse.Content.ReadAsStringAsync().Result;
                var viewModel = JsonConvert.DeserializeObject<ResponseList<BuildDefnDto>>(buildDefn);

                _builds.AddRange(
                    viewModel.Value.Select(buildDefnDto => new Tuple<Guid,int, BuildData>(source.Id, buildDefnDto.Id, new BuildData
                    {
                        BuildName = source.BuildName
                    })));
            }
        }

        public IEnumerable<BuildStoreEventArgs> GetBuildStoreEvents()
        {
            return from b in _builds
                let url =
                    $"{_buildConfigurationManager.TeamFoundationUrl}/DefaultCollection/{b.Item1}/_apis/build/builds?api-version={Version}&definitions={b.Item2}&$top=1"
                let buildResponse = _client.GetAsync(url).Result
                let buildValue = buildResponse.Content.ReadAsStringAsync().Result
                let dto = JsonConvert.DeserializeObject<ResponseList< BuildDto>>(buildValue)
                let first = dto.Value.FirstOrDefault()
                select new BuildStoreEventArgs
                {
                    Data = new BuildData
                    {
                        BuildName = b.Item3.BuildName,
                        BuildRequestedFor = first.RequestedFor.DisplayName,
                        EventType = BuildStoreEventType.Build,
                        Quality = "",
                        Status =  (BuildExecutionStatus)Enum.Parse(typeof(BuildExecutionStatus), first.Result ?? first.Status ?? "Unknown", true)
                    },
                    Type = BuildStoreEventType.Build
                };
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class ProjectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public string State { get; set; }

        public string Url { get; set; }

        public int Revision { get; set; }
    }

    public class ResponseList<T>
    {
        public int Count { get; set; }

        public T[] Value { get; set; }
    }

    public class BuildDto
    {
        public string BuildNumber { get; set; }
        public string Result { get; set; }

        public string Status { get; set; }

        public Person RequestedFor { get; set; }
    }

    public class Person
    {
        public string DisplayName { get; set; }
    }

    public class BuildDefnDto
    {
        public int Id { get; set; }
    }
}
