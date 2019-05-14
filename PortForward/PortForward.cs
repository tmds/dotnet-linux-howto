// Copyright 2019 Tom Deseyn <tom.deseyn@gmail.com>
// This file is made available under the MIT License.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SshUtils
{
    public class PortForwardException : System.Exception
    {
        public PortForwardException() { }
        public PortForwardException(string message, int statusCode) : base(message) {
            StatusCode = statusCode;
         }

        public int StatusCode { get; }
    }

    public class PortForwardOptions
    {
        public string User { get; set; }
        public string Host { get; set; }
        public string RemoteEndPoint { get; set; }
        public int TimeoutSeconds { get; set; } = 10;
        public bool AllowPasswordPrompt { get; set; }
        public string IdentityFile { get; set; }
    }

    class PortForward : IDisposable
    {
        private readonly Process _process;
        private readonly IPEndPoint _ipEndPoint;

        internal PortForward(Process process, IPEndPoint endPoint)
        {
            _process = process;
            _ipEndPoint = endPoint;
        }

        public void Dispose()
        {
            try
            {
                _process.Kill();
                _process.WaitForExit();
            }
            catch
            { }
            _process.Dispose();
        }

        public IPEndPoint IPEndPoint => _ipEndPoint;

        public static Task<PortForward> ForwardAsync(string remote /* [<user>@]<host>:<remoteendpoint> */, Action<PortForwardOptions> configure = null)
        {
            string host;
            string user = null;
            string remoteEndPoint;
            int indexOfAt = remote.IndexOf('@');
            int indexOfColon = remote.IndexOf(':');
            if (indexOfColon == -1)
            {
                throw new ArgumentException($"Missing remote end point", nameof(remote));
            }
            if (indexOfAt != -1)
            {
                user = remote.Substring(0, indexOfAt);
            }
            host = remote.Substring(indexOfAt + 1, indexOfColon - indexOfAt - 1);
            remoteEndPoint = remote.Substring(indexOfColon + 1);
            var portForwardOptions = new PortForwardOptions
            {
                RemoteEndPoint = remoteEndPoint,
                User = user,
                Host = host
            };
            configure?.Invoke(portForwardOptions);
            return ForwardAsync(portForwardOptions);
        }

        public static async Task<PortForward> ForwardAsync(PortForwardOptions options)
        {
            if (string.IsNullOrEmpty(options.RemoteEndPoint))
            {
                throw new ArgumentException($"{options.RemoteEndPoint} cannot be empty", nameof(options));
            }
            if (string.IsNullOrEmpty(options.Host))
            {
                throw new ArgumentException($"{options.Host} cannot be empty", nameof(options));
            }

            // Socket used to find a free port on the local machine.
            Socket portSocket = null;
            try
            {
                portSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                portSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                portSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = (portSocket.LocalEndPoint as IPEndPoint).Port;
                
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ssh",
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                if (!options.AllowPasswordPrompt)
                {
                    psi.ArgumentList.Add("-oBatchMode=yes");
                }

                if (!string.IsNullOrEmpty(options.User))
                {
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add(options.User);
                }

                if (!string.IsNullOrEmpty(options.IdentityFile))
                {
                    psi.ArgumentList.Add("-i");
                    psi.ArgumentList.Add(options.IdentityFile);
                }

                psi.ArgumentList.Add("-L");
                psi.ArgumentList.Add($"127.0.0.1:{port}:{options.RemoteEndPoint}");

                psi.ArgumentList.Add(options.Host);

                psi.ArgumentList.Add("sleep");
                psi.ArgumentList.Add(options.TimeoutSeconds.ToString());

                Process portForwardProcess = Process.Start(psi);

                try
                {
                    // wait until the port is forwarded
                    while (true)
                    {
                        if (portForwardProcess.HasExited)
                        {
                            if (portForwardProcess.ExitCode == 0)
                            {
                                throw new PortForwardException("Port forward process timed out", portForwardProcess.ExitCode);
                            }
                            else
                            {
                                throw new PortForwardException(portForwardProcess.StandardError.ReadToEnd(), portForwardProcess.ExitCode);
                            }
                        }
                        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                        {
                            try
                            {
                                socket.Connect("127.0.0.1", port);
                                break;
                            }
                            catch (SocketException)
                            {
                                await Task.Delay(50);
                            }
                        }
                    }

                    return new PortForward(portForwardProcess, new IPEndPoint(IPAddress.Loopback, port));
                }
                catch
                {
                    try
                    {
                        portForwardProcess.Kill();
                        portForwardProcess.WaitForExit();
                    }
                    catch {}
                    portForwardProcess.Dispose();
                    throw;
                }
            }
            finally
            {
                portSocket?.Dispose();
            }
        }
    }
}