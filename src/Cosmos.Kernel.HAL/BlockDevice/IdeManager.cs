// // This code is licensed under MIT license (see LICENSE for details)
//
// using Cosmos.Kernel.HAL.BlockDevice.IOGroup;
// using Cosmos.Kernel.HAL.Pci;
// using Cosmos.Kernel.HAL.Pci.Enums;
//
// namespace Cosmos.Kernel.HAL.BlockDevice;
//
// public class IdeManager
// {
//     private static readonly PciDevice? s_xDevice = PciManager.GetDeviceClass(ClassId.MassStorageController,
//         SubclassId.IdeInterface);
//
//     // These are common/fixed pieces of hardware. PCI, USB etc should be self discovering
//     // and not hardcoded like this.
//     // Furthermore some kind of security needs to be applied to these, but even now
//     // at least we have isolation between the consumers that use these.
//     /// <summary>
//     /// Primary ATA.
//     /// </summary>
//     public static readonly AtaIoGroup PrimaryAta = new(false);
//
//     /// <summary>
//     /// Secondary ATA.
//     /// </summary>
//     public static readonly AtaIoGroup SecondaryAta = new(true);
//
//     internal static void InitDriver()
//     {
//         if (s_xDevice != null)
//         {
//             s_xDevice.Claimed = true;
//             Console.WriteLine("Initializing ATA Primary Master...");
//             Initialize(Ata.ControllerIdEnum.Primary, Ata.BusPositionEnum.Master);
//             Console.WriteLine("Initializing ATA Primary Slave...");
//             Initialize(Ata.ControllerIdEnum.Primary, Ata.BusPositionEnum.Slave);
//             Console.WriteLine("Initializing ATA Secondary Master...");
//             Initialize(Ata.ControllerIdEnum.Secondary, Ata.BusPositionEnum.Master);
//             Console.WriteLine("Initializing ATA Secondary Slave...");
//             Initialize(Ata.ControllerIdEnum.Secondary, Ata.BusPositionEnum.Slave);
//         }
//
//     }
//
//     private static void Initialize(Ata.ControllerIdEnum aControllerId, Ata.BusPositionEnum aBusPosition)
//     {
//         AtaIoGroup xIo = aControllerId == Ata.ControllerIdEnum.Primary ? PrimaryAta : SecondaryAta;
//         var xAta = new ATA_PIO(xIo, aControllerId, aBusPosition);
//         if (xAta.DriveType == ATA_PIO.SpecLevel.Null)
//         {
//             return;
//         }
//         else if (xAta.DriveType == ATA_PIO.SpecLevel.ATA)
//         {
//             BlockDeviceManager.Devices.Add(xAta);
//             Ata.ataDebugger.Send("ATA device with speclevel ATA found.");
//         }
//         else if (xAta.DriveType == ATA_PIO.SpecLevel.ATAPI)
//         {
//             ATAPI atapi = new(xAta);
//             //TODO: Replace 1000000 with proper size once ATAPI driver implements it
//             //Add the atapi device to an array so we reorder them to be last
//             s_atapiDevices.Add(atapi);
//             s_atapiPartitions.Add(new Partition(atapi, 0, 1000000));
//             Ata.ataDebugger.Send("ATA device with speclevel ATAPI found");
//             return;
//         }
//
//         ScanAndInitPartitions(xAta);
//     }
//
//     internal static void ScanAndInitPartitions(BlockDevice device)
//     {
//         if (GPT.IsGPTPartition(device))
//         {
//             GPT xGpt = new(device);
//             BlockDeviceManager.Devices.Add(device);
//             Ata.ataDebugger.Send("Number of GPT partitions found:");
//             Ata.ataDebugger.SendNumber(xGpt.Partitions.Count);
//
//             foreach (GPT.GPartInfo part in xGpt.Partitions)
//             {
//                 BlockDeviceManager.Partitions.Add(new Partition(device, part.StartSector, part.SectorCount));
//             }
//         }
//         else
//         {
//             MBR mbr = new(device);
//
//             if (mbr.EBRLocation != 0)
//             {
//                 //EBR Detected
//                 byte[] xEbrData = new byte[512];
//                 device.ReadBlock(mbr.EBRLocation, 1U, ref xEbrData);
//                 EBR xEbr = new(xEbrData);
//
//                 for (int i = 0; i < xEbr.Partitions.Count; i++)
//                 {
//                     //var xPart = xEBR.Partitions[i];
//                     //var xPartDevice = new BlockDevice.Partition(xATA, xPart.StartSector, xPart.SectorCount);
//                     //Partition.Partitions.Add(xATA, xPartDevice);
//                 }
//             }
//
//         }
//     }
// }
