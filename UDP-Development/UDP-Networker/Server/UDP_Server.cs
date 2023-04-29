﻿using System.Net.Sockets;
using System.Net;
using Networker.Common;

namespace Networker.Server;

/*
 * Server and Client connect over TCP
 * Then the server gives the client a port to send to, and the client gives a port for the server to send to.
 * Then we use UDP from there with TCP critical write support
 */

/// <summary>
/// A server to client connection handle. Suport for TCP writes and UDP streaming
/// </summary>
public class Server : IDisposable
{
    private List<ClientHandle> _clients = new List<ClientHandle>();
    private uint _clientIDCounter = 0;
    private byte[] data;
    private TcpListener _clientRequestListener = new TcpListener(IPAddress.Any, Consts.LISTENING_PORT);
    private IPEndPoint? _listenIPEndPoint = new IPEndPoint(IPAddress.Any, Consts.LISTENING_PORT);

    public bool AcceptClients { get; set; } = true;
    public bool Disposed { get; private set; } = false;

    public Server()
    {
        Logger.Log("Starting server up", LogWarningLevel.Info);

        data = new byte[2048];

        _clientRequestListener.Start();
        Task.Run(ListenForClient);
    }

    private async void ListenForClient()
    {
        while (true)
        {
            while (!AcceptClients && !Disposed)
                await Task.Delay(1000);

            if (Disposed)
                return;

            TcpClient client = await _clientRequestListener.AcceptTcpClientAsync();
            HandleClientConnection(client);
        }
    }


    private void HandleClientConnection(TcpClient client)
    {
        if (Disposed)
            return;

        Logger.Log("Server got TCP connection", LogWarningLevel.Info);

        if (ValidateConnectionRequest(client, out var clientIP, out var userName))
        {
            ClientHandle newClient = new ClientHandle(userName, _clientIDCounter++, client, clientIP, 5678);
            _clients.Add(newClient);
        }
    }

    private unsafe bool ValidateConnectionRequest(TcpClient client, out IPEndPoint clientIP, out string userName)
    {
        Logger.Log("Trying to validate client request", LogWarningLevel.Info);

        clientIP = new IPEndPoint(0, 0);
        userName = "";

        if (!AcceptClients)
        {
            Logger.Log("Not accepting clients", LogWarningLevel.Info);
            client.Dispose();
            return false;
        }

        var stream = client.GetStream();
        stream.ReadTimeout = 1000; // Wait for data for one seconds
        int bytesRead = stream.Read(data, 0, 1024);

        try
        {
            ConnectionRequest request = Serializer.DeSerialize<ConnectionRequest>(data);

            clientIP = new IPEndPoint(request.IP, request.Port);
            userName = request.UserName;

            stream.Write(Consts.CONNECTION_ESTABLISHED);

            Logger.Log($"New client added, UserName: \"{userName}\"", LogWarningLevel.Succes);

            client.Dispose();
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("Unable to connect to client do to error", LogWarningLevel.Warning);
            client.Dispose();
            //throw;
            return false;
        }
    }

    public Packet[] HandleData()
    {
        List<Packet> packets = new List<Packet>();

        foreach (ClientHandle clientHandle in _clients)
        {
            uint clientID = clientHandle.ID;
            byte[] data = new byte[1024];
            int size = clientHandle.GetStreamedData(data);

            packets.Add(new Packet(data, false, size, clientID));
        }

        return packets.ToArray();
    }

    public ClientHandle[] GetCurrentClientHandles()
    {
        return _clients.ToArray();
    }

    public void Dispose()
    {
        Disposed = true;

        _clientRequestListener.Stop();

        foreach (ClientHandle clientHandle in _clients)
            clientHandle.Dispose();

        Disposed = true;
    }
}