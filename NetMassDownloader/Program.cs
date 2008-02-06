﻿/*------------------------------------------------------------------------------
 * SourceCodeDownloader - Kerem Kusmezer (keremskusmezer@gmail.com)
 *                        John Robbins (john@wintellect.com)
 * 
 * Download all the .NET Reference Sources and PDBs to pre-populate your 
 * symbol server! No more waiting as each file downloads individually while
 * debugging.
 * ---------------------------------------------------------------------------*/
#region Licence Information
/*       
 * http://www.codeplex.com/NetMassDownloader To Get The Latest Version
 *     
 * Copyright 2008 Kerem Kusmezer(keremskusmezer@gmail.com) And John Robbins (john@wintellect.com)
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License. 
 * You may obtain a copy of the License at
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using DownloadLibrary.Classes;
using System.Net;
using DownloadLibrary.Classes.Eula;
using System.Windows.Forms;
using DownloadLibrary.PEParsing;

namespace NetMassDownloader
{
    class Program
    {
        private static DownloaderArgParser argValues;

        // The number of files fully processed.
        private static int numProcessFiles;
        // The number of files where there were errors.
        private static int numNotProcessedFiles;
        // The number of source files downloaded.
        private static int numSourceFiles;

        /// <summary>
        /// The entry point to the whole application.
        /// </summary>
        /// <param name="args">
        /// The command line arguments to the program.
        /// </param>
        static int Main ( string [] args )
        {
            int retValue = 0;

            argValues = new DownloaderArgParser ( );

            // If there are no arguments, just return now.
            if ( args.Length == 0 )
            {
                argValues.OnUsage ( null );
                return ( 1 );
            }

            if ( true == argValues.Parse ( args ) )
            {
                argValues.Logo ( );

                try
                {
                    ProcessFiles ( );
                    ShowRunStats ( );
                }
                catch ( EulaNotAcceptedException )
                {
                    Console.WriteLine ( Constants.NotEulaFile );
                    retValue = 2;
                }
            }
            else
            {
                retValue = 1;
            }
            return ( retValue );
        }

        private static void ShowRunStats ( )
        {
            Console.WriteLine ( Constants.RunStatsFmt ,
                                numProcessFiles ,
                                numNotProcessedFiles ,
                                numSourceFiles );
        }

        static void ProcessFiles ( )
        {
            foreach ( String file in argValues.Files )
            {
                // Build up the output path.
                String finalPdbPath = argValues.Output;
                try
                {
                    PEFile peFile = new PEFile ( file );

                    Boolean doTheDownload = true;
                    if ( true == argValues.UsingSymbolCache )
                    {
                        finalPdbPath =
                                    CreateSymbolServerDirectory ( finalPdbPath ,
                                                                  peFile );
                    }

                    // The final pdb file.
                    String pdbToProcess = finalPdbPath + peFile.PdbFileName;

                    // We're downloading to a symbol server and not forcing, 
                    // look to see if the file already exists. If it does, skip 
                    // it.
                    if ( ( true == argValues.UsingSymbolCache ) &&
                         ( false == argValues.Force ) )
                    {
                        Boolean exists = File.Exists ( pdbToProcess );
                        if ( true == exists )
                        {
                            numNotProcessedFiles++;
                            doTheDownload = false;
                            Console.WriteLine (
                                        Constants.PdbAlreadyInSymbolServer ,
                                        Environment.NewLine ,
                                        file );
                        }
                    }

                    if ( true == doTheDownload )
                    {
                        Console.WriteLine ( Constants.DownloadingPdb ,
                                            Environment.NewLine ,
                                            file );

                        // Get the PDB file itself from the server.
                        MemoryStream resultStream =
                                  peFile.DownloadPDBFromServer ( finalPdbPath );
                        if ( null != resultStream )
                        {
                            PdbFileExtractor extract =
                                          new PdbFileExtractor ( pdbToProcess );

                            // If we are not extracting to a symbol server use
                            // the file paths in the PDB so everything works 
                            // with VS 2005.
                            extract.UseSourceFilePath = 
                                                    !argValues.UsingSymbolCache;

                            extract.SourceFileDownloaded += new PdbFileExtractor.
                                     SourceCodeHandler ( SourceFileDownloaded );

                            extract.EulaAcceptRequested += new PdbFileExtractor.
                                            EulaHandler ( EulaAcceptRequested );

                            String finalSrcPath = argValues.Output;

                            if ( true == argValues.UsingSymbolCache )
                            {
                                // Visual Studio is hardcoded to look at the 
                                // "src\source\.net\8.0" directory. I'm not sure
                                // how this will hold up in future versions of 
                                // VS and .NET reference source releases.
                                finalSrcPath += @"src\source\.net\8.0\";
                            }
                            extract.DownloadWholeFiles ( finalSrcPath );
                            numProcessFiles++;

                        }
                        else
                        {
                            numNotProcessedFiles++;
                            Console.WriteLine ( Constants.NoPdbFileFmt , file );
                        }
                    }
                }
                catch ( WebException )
                {
                    // Couldn't find it on the symbol server.
                    if ( true == argValues.UsingSymbolCache )
                    {
                        DeleteSymbolServerDirectory ( finalPdbPath );
                    }
                    numNotProcessedFiles++;
                    Console.WriteLine ( Constants.NotOnSymbolServer , file );
                }
                catch ( FileLoadException )
                {
                    // This is not a PE file.
                    if ( true == argValues.UsingSymbolCache )
                    {
                        DeleteSymbolServerDirectory ( finalPdbPath );
                    }
                    Console.WriteLine ( Constants.NotPEFileFmt , file );
                    numNotProcessedFiles++;
                }
                catch ( NoDebugSectionException )
                {
                    // There is not a .debug section in the PE file.
                    Console.WriteLine ( Constants.NoDebugSection , file );
                    numNotProcessedFiles++;
                }
            }
        }

        static void EulaAcceptRequested ( object sender , EulaRequestEvent e )
        {
            EulaDialog dlg = new EulaDialog ( e.EulaContent );
            DialogResult res = dlg.ShowDialog ( );
            e.EulaAccepted = ( res == DialogResult.Yes );
        }

        static void SourceFileDownloaded ( object sender ,
                                           SourceFileLoadEventArg e )
        {
            if ( null != e.OccurredException )
            {
                Console.WriteLine ( Constants.FileDownloadFailedFmt ,
                                    e.ToString ( ) );
            }
            else
            {
                if ( true == argValues.Verbose )
                {
                    Console.WriteLine ( Constants.DownloadedFileOfFmt ,
                                        e.FileLocationState ,
                                        Path.GetFileName ( e.FileName ) ,
                                        Path.GetDirectoryName ( e.FileName ) );
                }
                numSourceFiles++;
            }
        }

        /// <summary>
        /// Deletes the final symbol server directory for a failed download.
        /// </summary>
        /// <param name="finalPdbDirectory">
        /// The complete directory name including GUID.
        /// </param>
        static void DeleteSymbolServerDirectory ( String finalPdbDirectory )
        {
            // I need to delete the directory above the final directory and this
            // allows me to do that. :)
            String parentDir = finalPdbDirectory + "..\\";
            if ( true == Directory.Exists ( finalPdbDirectory ) )
            {
                // Delete the GUID directory.
                Directory.Delete ( finalPdbDirectory );
                // Delete the FOO.PDB directory.
                Directory.Delete ( parentDir );
            }
        }

        /// <summary>
        /// Creates and returns the full symbol path where the PDB file needs 
        /// to be downloaded to.
        /// </summary>
        /// <param name="symbolServer">
        /// The root symbol server cache directory.
        /// </param>
        /// <param name="peFileInfo">
        /// The <see cref="PEFile"/> class describing the loaded PE file.
        /// </param>
        /// <returns>
        /// The full path name for the PDB file location.
        /// </returns>
        static String CreateSymbolServerDirectory ( String symbolServer ,
                                                    PEFile peFileInfo )
        {
            // Append on the PDB name and GUID since this is going into the 
            // symbol server. If there's any path information on the PDB file,
            // I'll only do the filename itself.
            String pdbFile = peFileInfo.PdbFileName;
            String currPath = symbolServer + pdbFile + "\\";
            currPath += peFileInfo.PdbVersion;
            currPath += "\\";
            if ( false == Directory.Exists ( currPath ) )
            {
                Directory.CreateDirectory ( currPath );
            }
            return ( currPath );
        }
    }
}