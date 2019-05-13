// Copyright 2019 Tom Deseyn <tom.deseyn@gmail.com>
// This file is made available under the MIT License.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace console
{
    [System.Serializable]
    public class PortForwardException : System.Exception
    {
        public PortForwardException() { }
        public PortForwardException(string message, int statusCode) : base(message) {
            StatusCode = statusCode;
         }

        public int StatusCode { get; }
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

        public static async Task<PortForward> ForwardAsync(string remoteUser, string remoteHost, string remoteSocket, int timeoutSeconds = 10, bool allowPasswordPrompt = false)
        {
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
                    ArgumentList = { "-l", remoteUser, "-L", $"127.0.0.1:{port}:{remoteSocket}", remoteHost, "sleep", timeoutSeconds.ToString() },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                if (!allowPasswordPrompt)
                {
                    psi.ArgumentList.Add("-oBatchMode=yes");
                }

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