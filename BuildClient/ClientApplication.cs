using System;
using System.Threading.Tasks;
using Autofac;
using BuildClient.Configuration;
using BuildCommon;

namespace BuildClient
{
    internal class ClientApplication : IDisposable
    {
        private BuildManager _buildManager;
        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method. 
            // Therefore, you should call GC.SupressFinalize to 
            // take this object off the finalization queue 
            // and prevent finalization code for this object 
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        public void Start()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (s, e) =>
                Tracing.Server.TraceError(
                    "Server-ApplicationException-An unhandled application exception occured-details {0}",
                    e.ExceptionObject.ToString());

            IContainer container = LoadContainer();
            _buildManager = container.Resolve<BuildManager>();

            var startupTask = new Task(() =>
                {
                    Tracing.Server.TraceInformation("Starting Build Manager");
                    _buildManager.StartProcessing(null);
                });
            
            startupTask.Start();
        }

        private static IContainer LoadContainer()
        {
            try
            {
                var builder = new ContainerBuilder();
                builder.RegisterType<BuildConfigurationManager>().As<IBuildConfigurationManager>();
                builder.RegisterType<Notifier>().As<INotifier>();
                builder.RegisterType<VsoBridge>().As<IBuildEventSystem>();
                builder.RegisterType<BuildStoreEventSource>().As<IBuildStoreEventSource>().SingleInstance();
                builder.Register(c => new TfsServiceProvider(c.Resolve<IBuildConfigurationManager>().TeamFoundationUrl,c.Resolve<IBuildConfigurationManager>()))
                       .As<IServiceProvider>().SingleInstance();
                builder.RegisterType<BuildStatusChangeChannelManager>()
                       .As<ICachedChannelManager<IBuildStatusChange>>()
                       .SingleInstance();
                builder.RegisterType<ChannelFactoryBuildPublisher>().As<IBuildEventPublisher>();
                builder.Register(
                    c =>
                    new BuildManager(c.Resolve<IBuildConfigurationManager>(),
                                     c.Resolve<IBuildStoreEventSource>(), c.Resolve<IBuildEventPublisher>()))
                       .SingleInstance();

                return builder.Build();
            }
            catch (Exception ex)
            {
                ex.ThrowIfReflectionTypeLoadThrowResolutionException();
                throw;
            }
        }


        public void Stop()
        {
            if (_buildManager != null)
            {
                var task=new Task(() => _buildManager.StopProcessing());
                task.WithContinuation(()=>Tracing.Server.TraceInformation("Build Manager Stopped"),
                                                null,
                                                null);
                task.Start();
           }

        }


        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called. 
            if (!_disposed)
            {
                // If disposing equals true, dispose all managed 
                // and unmanaged resources. 
                if (disposing)
                {
                    // Dispose managed resources.
                    if (_buildManager != null)
                    {
                        _buildManager.Dispose();
                    }
                }

                _buildManager = null;

                // Note disposing has been done.
                _disposed = true;
            }
        }
    }
}