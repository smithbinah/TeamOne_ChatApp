﻿using ChatBase.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace ChatBase {
    public class Server : INotifyPropertyChanged {
        // Variables to allow listening
        private TcpListener tcpListener;
        private Thread listenThread;

        // Variables that keep client state
        private int connClients = 0;
        private List<Thread> clientThreads = new List<Thread>();
        private List<TcpClient> clients = new List<TcpClient>();
        private int nextClientID = 1;

        //private bool isClosing = false;
        private List<bool> clientListening = new List<bool>();
        private bool isClosing = true;
        private string messagesReceived = "";

        public event PropertyChangedEventHandler PropertyChanged;

        public string MessagesReceived {
            get => messagesReceived;
            set {
                messagesReceived = value;
                PropChanged();
            }
        }
        public int ConnClients {
            get => connClients;
            set {
                connClients = value;
                PropChanged();
            }
        }

        /// <summary>
        /// Start the server
        /// </summary>
        public void Start() {
            // Change to IpAddress.Any for internet communication
            tcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), Constants.PORT);
            listenThread = new Thread(new ThreadStart(ListenForClients));
            listenThread.Start();
        }

        /// <summary>
        /// Begin listening for client connections and create new TcpClients if one does connect
        /// </summary>
        private void ListenForClients() {
            tcpListener.Start();

            // Do not stop until the server is closed
            while (true) {
                // Block until client has connected
                TcpClient client = tcpListener.AcceptTcpClient();

                // Create thread to handle communication with client
                ConnClients++;

                Thread clientThread = new Thread(new ParameterizedThreadStart(ClientCommsHandler));
                clientThreads.Add(clientThread);
                clients.Add(client);
                clientThread.Start(client);
            }
        }

        /// <summary>
        /// Listen for messages from the client
        /// </summary>
        /// <param name="client">TcpClient to listen to</param>
        private void ClientCommsHandler(object client) {
            TcpClient tcpClient = (TcpClient)client;
            NetworkStream clientStream = tcpClient.GetStream();
            int clientID = nextClientID++;
            clientListening.Add(true);

            byte[] msg = new byte[Constants.BUFFER_SIZE];
            int bytesRead;

            // Send client their clientID
            Packet cidMessage = Constants.CLIENT_ID_TEMPLATE.AlterContent(clientID.ToString());
            SendPacket(cidMessage, clientStream);

            Thread.Sleep(150);  // Need to wait a bit because it will be one big message if we don't
            string welcomeMessage = "You are client " + clientID + Environment.NewLine;
            SendPacket(Constants.MESSAGE_TEMPLATE.AlterContent(welcomeMessage), clientStream);
            WriteMessage("Client " + clientID + " has connected!");

            // Listen for client response
            while (clientListening[clientID-1]) {
                bytesRead = 0;

                try {
                    // Block until message received
                    bytesRead = clientStream.Read(msg, 0, Constants.BUFFER_SIZE);
                } catch (SocketException sockEx) {
                    if (isClosing) {
                        // Don't do anything because the server is stopping
                        break;
                    }
                    else {
                        MessageBox.Show(sockEx.ToString());
                        break;
                    }
                } catch (Exception ex) {
                    // TODO: get better exceptions
                    //MessageBox.Show(ex.ToString());
                    break;
                }

                // Convert bytes to string and display string
                string message = Encoding.UTF8.GetString(msg, 0, bytesRead);

                //Packet tempPacket;
                if (Packet.JsonToPacket(message, out Packet tempPacket)) {
                    ReadPacket(tempPacket, tcpClient, clientID);
                }
                else {
                    Console.WriteLine("ERROR: INVLAID JSON... MESSAGE: {0}", message);
                }
            }

            tcpClient.Close();
        }

        /// <summary>
        /// Read a received Packet and perform action based on type
        /// </summary>
        /// <param name="json">Json string of Packet</param>
        private void ReadPacket(Packet p, TcpClient tcpClient, int clientID) {
            switch (p.Type) {
                case PacketType.Message:
                    Packet newPacket = Constants.MESSAGE_TEMPLATE.AlterContent("Client " + clientID + " says: " + p.Content);
                    newPacket.Args["Owner"] = clientID.ToString();
                    newPacket.Args["Room"] = 0.ToString();
                    Broadcast(newPacket);      // Broadcast message to all clients
                    WriteMessage(newPacket.Content);
                    break;
                case PacketType.Goodbye:
                    Packet leaveMsg = Constants.MESSAGE_TEMPLATE.AlterContent("Client " + clientID + " has left...");
                    Broadcast(leaveMsg);
                    WriteMessage(leaveMsg.Content);
                    ConnClients--;
                    clients.Remove(tcpClient);
                    clientListening[clientID-1] = false;
                    break;
                default:
                    Console.WriteLine("ERROR: wrong packet type........... Content: {0}", p.Content);
                    break;
            }
        }

        /// <summary>
        /// Write message to log textbox
        /// </summary>
        /// <param name="msg"></param>
        private void WriteMessage(string msg) {
            MessagesReceived += msg + Environment.NewLine;
        }

        /// <summary>
        /// Send a packet to a client
        /// </summary>
        /// <param name="packet">Packet to send</param>
        /// <param name="clientStream">Client to send to</param>
        private void SendPacket(Packet packet, NetworkStream clientStream) {
            string json = packet.ToJsonString();

            byte[] buffer = Encoding.UTF8.GetBytes(json);
            clientStream.Write(buffer, 0, buffer.Length);
            clientStream.Flush();
        }

        /// <summary>
        /// Send packet to all connected clients
        /// </summary>
        /// <param name="p">Packet to send</param>
        private void Broadcast(Packet p) {
            // Echo to all dudes
            foreach (TcpClient client in clients) {
                SendPacket(p, client.GetStream());
            }
        }

        /// <summary>
        /// When server is closed, dispose all the things
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void Window_Closed(object sender, EventArgs e) {
            isClosing = true;

            // Send server bye message to clients so they know that the server is shutting down
            foreach (TcpClient cl in clients) {
                SendPacket(Constants.SERVER_BYE_PACKET, cl.GetStream());
                cl.Close();
            }
            
            tcpListener.Stop();
            listenThread.Abort();
        }

        /// <summary>
        /// If we are in the process of closing, let keep it in state
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            isClosing = true;
        }

        /// <summary>
        /// If a property is changed, invoke PropertyChanged event
        /// </summary>
        /// <param name="prop"></param>
        private void PropChanged([CallerMemberName] string prop = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}
