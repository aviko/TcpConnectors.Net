﻿using System;
using System.Threading.Tasks;

namespace TcpConnectors
{
    partial class ServerConnectorContext
    {
        internal class RequestResponseData
        {
            internal int RequestId { get; set; }
            internal int Module { get; set; }
            internal int Command { get; set; }
            internal object Packet { get; set; }
        }

        private ServerConnectors _serverConnectors;
        internal long _lastRecievedLeepAliveTimestamp;
        internal DateTime _connectedTime = default(DateTime);

        internal ServerConnectorContext(int id, ServerConnectors serverConnectors)
        {
            Id = id;
            _serverConnectors = serverConnectors;
            _connectedTime = DateTime.UtcNow;
        }

        internal void OnRecv(byte[] buf)
        {
            try
            {
                if (buf[0] == 0) //request response packet
                {
                    object reqPacket = null;
                    int requestId = 0;
                    string exceptionMsg = null;
                    byte module = 0;
                    byte command = 0;
                    try
                    {
                        reqPacket = ConnectorsUtils.DeserializeRequestPacket(buf, _serverConnectors._settings.PacketsMap, out requestId, out module, out command);
                    }
                    catch (Exception ex) { exceptionMsg = ex.Message; }


                    if (buf[1] == 0 && requestId == 0) //keep alive
                    {
                        _lastRecievedLeepAliveTimestamp = (long)reqPacket;
                        //Console.WriteLine($"keep alive: {reqPacket}");
                        _serverConnectors.TriggerOnDebugLog(this, DebugLogType.OnKeepAlive, reqPacket.ToString());
                        return;
                    }


                    if (exceptionMsg != null)
                    {
                        var resBuf = ConnectorsUtils.SerializeRequestPacket(0, 1, exceptionMsg, requestId);
                        TcpSocketsUtils.Send(Socket, resBuf, OnSend, OnExcp);
                    }
                    else
                    {
                        var rrData = new RequestResponseData()
                        {
                            RequestId = requestId,
                            Module = module,
                            Command = command,
                            Packet = reqPacket,
                        };
                        new Task(() => HandleRequestResponse(rrData)).Start();
                    }
                }
                else //packet
                {
                    var packet = ConnectorsUtils.DeserializePacket(buf, _serverConnectors._settings.PacketsMap, out byte module, out byte command);
                    _serverConnectors.TriggerOnPacket(this, module, command, packet);
                }
            }
            catch (Exception ex)
            {
                _serverConnectors.TriggerOnDebugLog(this, DebugLogType.OnRecvException, ex.ToString());
            }
        }

        private void HandleRequestResponse(RequestResponseData rrData)
        {
            try
            {
                var res = _serverConnectors.TriggerOnRequestPacket(this, rrData.Module, rrData.Command, rrData.Packet);
                var resBuf = ConnectorsUtils.SerializeRequestPacket(rrData.Module, rrData.Command, res, rrData.RequestId);
                TcpSocketsUtils.Send(Socket, resBuf, OnSend, OnExcp);
            }
            catch (Exception ex)
            {
                var resBuf = ConnectorsUtils.SerializeRequestPacket(0, 1, ex.Message, rrData.RequestId);
                TcpSocketsUtils.Send(Socket, resBuf, OnSend, OnExcp);
            }
        }

        internal void OnSend()
        {
            _serverConnectors.TriggerOnDebugLog(this, DebugLogType.OnSend, "");
            //Console.WriteLine("ServerConnectorContext.OnSend()");
        }

        internal void OnExcp(Exception ex)
        {
            if (Socket.Connected)
            {
                _serverConnectors.TriggerOnException(this, ex);
            }
            else
            {
                //not connected
                if (_serverConnectors._contextMap.TryRemove(this.Id, out var removed))
                {
                    _serverConnectors.TriggerOnDisconnect(this);
                }
            }

        }
    }
}
