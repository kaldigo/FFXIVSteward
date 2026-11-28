namespace Steward;

public sealed partial class Plugin
{
    private ulong connectionCharacter;
    private bool connectionRequested;
    private bool storageRoutingRequested;
    private long nextHeartbeat;

    private void OnConnectionRequested(ulong character)
    {
        if(!disposed&&character!=0&&character==player.ContentId)
            connectionRequested=true;
    }

    private void MaintainConnection()
    {
        var character=player.ContentId;
        if(character==0||!config.Connected)return;

        if(connectionCharacter!=character)
        {
            connectionCharacter=character;
            connectionRequested=true;
        }

        var now=Environment.TickCount64;
        var pulseDue=attached==character&&(now>=nextHeartbeat||connectionRequested);
        if(!pulseDue&&!connectionRequested&&!storageRoutingRequested)return;

        if(pulseDue)
        {
            nextHeartbeat=now+1000;
            var pulse=pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Heartbeat");
            if(!pulse.HasFunction||!pulse.InvokeFunc(character,Endpoint))
            {
                attached=0;
                connectionRequested=true;
            }
            else CheckCompanionFault(character);
        }

        if(connectionRequested)
        {
            connectionRequested=false;
            if(attached!=character)
            {
                var version=pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.Version");
                if(!version.HasFunction){status="Waiting for AutoRetainer integration";return;}
                if(version.InvokeFunc()!=6)
                    throw new InvalidOperationException("Update both plugins: AutoRetainer integration version 6 is required");
                if(pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.StorageVersion").InvokeFunc()<5)
                    throw new InvalidOperationException("Update AutoRetainer: safe standalone entrust and venture mode is required");
                if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Connect").InvokeFunc(character,Endpoint))
                {status="AutoRetainer declined the connection";return;}
                attached=character;
                nextHeartbeat=now+1000;
                storageRoutingRequested=true;
            }
        }

        if(attached!=character)return;
        if(storageRoutingRequested)
        {
            storageRoutingRequested=false;
            // Keep permission unavailable until AutoRetainer confirms the storage role.
            attached=0;
            if(!pi.GetIpcSubscriber<ulong,ulong,bool>("AutoRetainer.InventoryPolicy.MigrateStorageRouting")
                .InvokeFunc(character,config.Storage.Enabled?config.Storage.NonmarketableRetainer:0))
                throw new InvalidOperationException("AutoRetainer storage-role migration is not ready");
            attached=character;
            CheckCompanionFault(character);
        }
        status=config.LogOnly?"Log only: proposed actions recorded; all item actions blocked":
            config.ExecutionEnabled?"Enabled features follow AutoRetainer":"Item actions held for review";
    }

    private void CheckCompanionFault(ulong character)
    {
        if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(character))
            HoldExecution("AutoRetainer has an unresolved inventory transaction");
    }
}
