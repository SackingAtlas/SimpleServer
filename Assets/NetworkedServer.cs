using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    const int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    const int socketPort = 5491;
    LinkedList<PlayerAccount> playerAccounts;
    string playerAccountFilePath;
    int playerWaitingForMatch = -1;
    LinkedList<GameSession> gameSessions;
    int playCount = 0;
    int[] CellButtons = new int[9];

    // Start is called before the first frame update
    void Start()
    {
        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";

        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();

        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);

        HostTopology topology = new HostTopology(config, maxConnections);

        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccounts = new LinkedList<PlayerAccount>();
        gameSessions = new LinkedList<GameSession>();

        LoadPlayerAccounts();

    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if(signifier == ClientToServerSignifiers.CreatAccount)
        {
            string n = csv[1];
            string p = csv[2];

            bool isUnique = false;
            foreach(PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    isUnique = true;
                    break;
                }
            }
            if (!isUnique)
            {
                playerAccounts.AddLast(new PlayerAccount(n, p));

                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);

                SavePlayerAccounts();
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameInUse, id);
            }
        }
        else if(signifier == ClientToServerSignifiers.login)
        {
            string n = csv[1];
            string p = csv[2];
            bool hasBeenFound = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    if(pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureIncorrectPassword, id);
                    }

                    hasBeenFound = true;
                    break;
                }
            }
            if (!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameNotFound, id);
            }
        }
        else if (signifier == ClientToServerSignifiers.AddToGameSessionQueue)
        {
            if (playerWaitingForMatch == -1)
            {
                playerWaitingForMatch = id;
            }
            else
            {
                GameSession gs = new GameSession(playerWaitingForMatch, id, 0);
                gameSessions.AddLast(gs);
                int turn1 = 1;
                int turn2 = 2;
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + "," + turn1, id);
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + "," + turn2, playerWaitingForMatch);

                playerWaitingForMatch = -1;
            }
        }
        else if (signifier == ClientToServerSignifiers.TicTacToePlay)
        {
            int lastPlay = int.Parse(csv[1]);
            CellButtons[playCount] = lastPlay;
           ++playCount;
            GameSession gs = FindGamSessionWithPlayerID(id);

            if (gs.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "," + lastPlay, gs.playerID2);
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "," + lastPlay, gs.observerID);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "," + lastPlay, gs.playerID1);
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "," + lastPlay, gs.observerID);
            }
                
        }
        else if (signifier == ClientToServerSignifiers.AddOberverToSession)
        {
            //string gameSessionName = gameSessions.Last.ToString();
            GameSession gs = gameSessions.Last.Value;
            gs.observerID = id;
            SendMessageToClient(ServerToClientSignifiers.ObserverEntered + "," + playCount , id);
            for (int i = 0; i < playCount; i++)
            {
                SendMessageToClient(ServerToClientSignifiers.ObserverCatchUp + "," + CellButtons[i], id);
            }
        }
        else if (signifier == ClientToServerSignifiers.ReplayRequest) 
        {
            for (int i = 0; i < playCount; i++)
            {
                SendMessageToClient(ServerToClientSignifiers.Replay + "," + CellButtons[i], id);
            }
        }
        else if (signifier == ClientToServerSignifiers.CommunicationPassing)
        {
            int MessagePassed = int.Parse(csv[1]);
            GameSession gs = FindGamSessionWithPlayerID(id);

            if (gs.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.PassedCommunication + "," + MessagePassed, gs.playerID2);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.PassedCommunication + "," + MessagePassed, gs.playerID1);
            }
        }
}

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(playerAccountFilePath);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(pa.name + "," + pa.password);
        }
        sw.Close();
    }
    private void LoadPlayerAccounts()
    {
        if (File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountFilePath);

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                PlayerAccount pa = new PlayerAccount(csv[0], csv[1]);
                playerAccounts.AddLast(pa);
            }
        }
    }
    private GameSession FindGamSessionWithPlayerID(int id)
    {
        foreach(GameSession gs in gameSessions)
        {
            if(gs.playerID1 == id || gs.playerID2 == id)
                return gs;
        }
        return null;
    }
}

public class PlayerAccount
{
    public string name, password;
    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public class GameSession
{
    public int playerID1, playerID2, observerID;

    public GameSession(int PlayerID1, int PlayerID2, int ObserverID)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        observerID = ObserverID;
    }
}

public static class ClientToServerSignifiers
{
    public const int login = 1;
    public const int CreatAccount = 2;
    public const int AddToGameSessionQueue = 3;
    public const int TicTacToePlay = 4;
    public const int AddOberverToSession = 5;
    public const int ReplayRequest = 6;
    public const int CommunicationPassing = 7;
}

public static class ServerToClientSignifiers
{
    public const int LoginResponse = 1;
    public const int GameSessionStarted = 2;
    public const int OpponentTicTacToePlay = 3;
    public const int ObserverEntered = 4;
    public const int ObserverCatchUp = 5;
    public const int Replay = 6;
    public const int PassedCommunication = 7;
}

public static class LoginResponses
{
    public const int Success = 1;
    public const int FailureNameInUse = 2;
    public const int FailureNameNotFound = 3;
    public const int FailureIncorrectPassword = 4;
}

public static class SquarePlayedIn
{
    public const int TopLeft = 1;
    public const int TopCenter = 2;
    public const int TopRight = 3;
    public const int MiddleLeft = 4;
    public const int MiddleCenter = 5;
    public const int MiddleRight = 6;
    public const int BottomLeft = 7;
    public const int BottomCenter = 8;
    public const int BottomRight = 9;
}