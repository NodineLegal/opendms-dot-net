﻿/* Copyright 2011 the OpenDMS.NET Project (http://sites.google.com/site/opendmsnet/)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using Common.Data;

namespace HttpModule.Storage
{
    /// <summary>
    /// Represents the controlling object for file system access.
    /// </summary>
    public class Master
    {   
        // Howto : make it pluggable http://weblogs.asp.net/justin_rogers/articles/61042.aspx

        /// <summary>
        /// A reference to <see cref="Common.FileSystem.IO"/> allowing file system access.
        /// </summary>
        private Common.FileSystem.IO _fileSystem;

        /// <summary>
        /// Initializes a new instance of the <see cref="Master"/> class.
        /// </summary>
        /// <param name="fileSystem">A reference to <see cref="Common.FileSystem.IO"/> allowing file system access.</param>
        public Master(Common.FileSystem.IO fileSystem)
        {
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Loads a <see cref="Common.Data.MetaAsset"/> from the file system without checking locks or applying locks.
        /// </summary>
        /// <param name="guid">The identifier for the resource to load</param>
        /// <param name="ma">The instantiated <see cref="Common.Data.MetaAsset"/> that was loaded from disk.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, IOError, SerializationError or InstantiationError.
        /// </returns>
        /// <remarks>
        /// Use care when calling this method as it loads the <see cref="Common.Data.MetaAsset"/> without
        /// concern for locks, without applying locks and without checking any security constraints.
        /// </remarks>
        public ResultType LoadMetaLite(Guid guid, out MetaAsset ma, out string errorMessage)
        {
            errorMessage = null;
            ma = new MetaAsset(guid, _fileSystem);

            if (!ma.ResourceExistsOnFilesystem())
                return ResultType.NotFound;

            if (!ma.Load())
            {
                Common.Logger.General.Error("Failed to load the meta asset.");
                errorMessage = "The meta asset could not be loaded.";
                return ResultType.IOError;
            }

            return ResultType.Success;
        }

        /// <summary>
        /// Loads a <see cref="Common.Data.MetaAsset"/> from the file system checking locks and applying locks if instructed.
        /// Deserializes the MetaAsset from disk into a Common.Data.MetaAsset object in memory applying a resource
        /// lock if instructed.  Returns:
        /// </summary>
        /// <param name="guid">The identifier for the resource to load.</param>
        /// <param name="applyLock"><c>True</c> if a lock is to be applied to a resource.</param>
        /// <param name="checkLock">if set to <c>true</c> the lock should be checked.</param>
        /// <param name="requestingUser">The requesting user.</param>
        /// <param name="readOnly">if set to <c>true</c> the user is requesting read-only access.</param>
        /// <param name="ma">The instantiated <see cref="Common.Data.MetaAsset"/> that was loaded from disk.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, IOError, SerializationError, InstantiationError or ResourceIsLocked.
        /// </returns>
        public ResultType LoadMeta(Guid guid, bool applyLock, bool checkLock, string requestingUser, bool readOnly, out MetaAsset ma, out string errorMessage)
        {
            ResultType result;

            // Load the MA
            if ((result = LoadMetaLite(guid, out ma, out errorMessage)) != ResultType.Success)
                return result;

            // Check to see if the user can access the resource (compares locks and readonly flags)
            if(checkLock && !CanUserAccessResource(ma, requestingUser, readOnly, out errorMessage))
                return ResultType.ResourceIsLocked;

            // Apply the lock if requested
            if (applyLock)
            {
                if ((result = ApplyLock(ma, requestingUser, out errorMessage)) != ResultType.Success)
                    return result;
            }

            return ResultType.Success;
        }

        /// <summary>
        /// Determines if the specified requesting user can access the resource.
        /// </summary>
        /// <param name="ma">The <see cref="Common.Data.MetaAsset"/> to check accessability.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="readOnly"><c>True</c> if the user only wants read-only access.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        ///   <c>True</c> if the user can access the resource; otherwise, <c>false</c>.
        /// </returns>
        public bool CanUserAccessResource(MetaAsset ma, string requestingUser, bool readOnly, out string errorMessage)
        {
            errorMessage = null;

            if (!ma.IsLocked)
                return true;
            
            // Lock Exists
            if (ma.LockedBy != requestingUser && !readOnly)
            {
                // Not locked by this user and the request is not read-only
                errorMessage = "The resource cannot be accessed as it is in use by " + ma.LockedBy +
                    " since " + ma.LockedAt.ToString();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applys a lock to the specified <see cref="Common.Data.MetaAsset"/>.
        /// </summary>
        /// <param name="ma">The <see cref="Common.Data.MetaAsset"/> on which to apply the lock.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success
        /// </returns>
        public ResultType ApplyLock(MetaAsset ma, string requestingUser, out string errorMessage)
        {
            errorMessage = null;

            // Apply the lock to the in-memory object
            ma.ApplyLock(DateTime.Now, requestingUser);

            ma.Save();

            // The persistant store now has the lock

            return ResultType.Success;
        }

        /// <summary>
        /// Applys a lock to the <see cref="Common.Data.MetaAsset"/> corresponding to the argument <see cref="Guid"/>.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> representing the unique identifier of the resource.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, IOError, SerializationError, InstantiationError or ResourceIsLocked.
        /// </returns>
        public ResultType ApplyLock(Guid guid, string requestingUser, out string errorMessage)
        {
            MetaAsset ma;

            return LoadMeta(guid, true, true, requestingUser, false, out ma, out errorMessage);
        }

        /// <summary>
        /// Releases a lock to the specified <see cref="Common.Data.MetaAsset"/>.
        /// </summary>
        /// <param name="ma">The <see cref="Common.Data.MetaAsset"/> on which to release the lock.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, PermissionsError, IOError or SerializationError.
        /// </returns>
        public ResultType ReleaseLock(MetaAsset ma, string requestingUser, out string errorMessage)
        {
            errorMessage = null;

            // Apply the lock to the in-memory object
            ma.ReleaseLock();

            ma.Save();

            return ResultType.Success;
        }

        /// <summary>
        /// Releases a lock to the <see cref="Common.Data.MetaAsset"/> corresponding to the argument <see cref="Guid"/>.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> representing the unique identifier of the resource.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, PermissionsError, IOError or SerializationError.
        /// </returns>
        public ResultType ReleaseLock(Guid guid, string requestingUser, out string errorMessage)
        {
            ResultType result;
            MetaAsset ma;

            if ((result = LoadMetaLite(guid, out ma, out errorMessage)) != ResultType.Success)
                return result;

            return ReleaseLock(ma, requestingUser, out errorMessage);
        }

        /// <summary>
        /// Gets a meta asset from the file system.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> representing the unique identifier of the resource.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="readOnly"><c>True</c> if the user only wants read-only access.</param>
        /// <param name="ma">The instantiated <see cref="Common.Data.MetaAsset"/> that was loaded from disk.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, IOError, SerializationError, InstantiationError or ResourceIsLocked.
        /// </returns>
        public ResultType GetMeta(Guid guid, string requestingUser, bool readOnly, out MetaAsset ma, out string errorMessage)
        {
            return LoadMeta(guid, !readOnly, true, requestingUser, readOnly, out ma, out errorMessage);
        }

        /// <summary>
        /// Gets a stream allowing access to the data resource.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> representing the unique identifier of the resource.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="readOnly"><c>True</c> if the user only wants read-only access.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <param name="iostream">The iostream.</param>
        /// <returns>
        /// Success, IOError, SerializationError, InstantiationError or ResourceIsLocked.
        /// </returns>
        public ResultType GetData(Guid guid, string requestingUser, bool readOnly, out string errorMessage, out Common.FileSystem.IOStream iostream)
        {
            ResultType result;
            Common.Data.MetaAsset ma;
            Common.Data.DataAsset da;

            errorMessage = null;
            iostream = null;

            // Load and checks everything applying the lock if !readonly
            if ((result = LoadMeta(guid, !readOnly, true, requestingUser, false, out ma, out errorMessage)) != ResultType.Success)
                return result;

            da = new DataAsset(ma, _fileSystem);

            iostream = da.GetReadStream();

            if (iostream == null)
                return ResultType.IOError;

            return ResultType.Success;
        }

        /// <summary>
        /// Saves a <see cref="Common.Data.MetaAsset"/> to the file system.  This method ignores any version (meta or data) set within the argument 'ma'.
        /// </summary>
        /// <param name="ma">The <see cref="Common.Data.MetaAsset"/> to save.</param>
        /// <param name="requestingUser">The user requesting access to the resource.</param>
        /// <param name="releaseLock"><c>True</c> to release the lock on the asset after saving.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>
        /// Success, Failure, PermissionsError, InstantiationError, ResourceIsLocked, IOError or SerializationError.
        /// </returns>
        public ResultType SaveMeta(MetaAsset ma, string requestingUser, bool releaseLock, out string errorMessage)
        {
            // The version comes in as the current version and is incremented and saved back with the
            // next version which is actually the new current version
            Version currentVersion;
            ResultType result;
            MetaAsset currentMa;
            DataAsset currentDa;

            errorMessage = null;

            // Get MA
            result = LoadMeta(ma.Guid, false, true, requestingUser, false, out currentMa, out errorMessage);

            // Get DA
            currentDa = new DataAsset(currentMa, _fileSystem);

            // Get Version
            currentVersion = new Version(new FullAsset(currentMa, currentDa));

            if (result == ResultType.NotFound)
            {
                // The meta asset could not be located, this means its a new asset and as such, should be created

                try
                {
                    ma.UpdateByServer(new ETag("1"), 1, 0, requestingUser, DateTime.Now, ma.Creator, ma.Length, 
                        ma.Md5, ma.Created, ma.Modified, ma.LastAccess);
                }
                catch (Exception e)
                {
                    errorMessage = e.Message;
                    return ResultType.Failure;
                }
            }
            else if (result == ResultType.Success)
            {
                // Exists already

                // Release any locks on the current ma because if we are here, any existing lock belongs to this user
                // and we do not want the resource in history locked for future users.
                if (ma.IsLocked)
                {
                    if ((result = ReleaseLock(currentMa, requestingUser, out errorMessage)) != ResultType.Success)
                        return result;
                }

                // Copy using the version scheme - copies the meta asset
                if (!currentVersion.CopyMetaUsingVersionScheme())
                {
                    errorMessage = "Failed to copy the current meta asset into version history.";
                    return ResultType.IOError;
                }

                currentVersion.IncrementMetaVersion();
                ma.UpdateByServer(currentVersion.MetaAsset.ETag, currentVersion.MetaAsset.MetaVersion, currentVersion.MetaAsset.DataVersion, requestingUser,
                    DateTime.Now, ma.Creator, ma.Length, ma.Md5, ma.Created, ma.Modified, ma.LastAccess);
            }
            else // error
                return result;

            if (releaseLock)
                ma.ReleaseLock();
            else
                ma.ApplyLock(DateTime.Now, requestingUser);

            // Save MetaAsset
            ma.Save();

            return ResultType.Success;
        }

        /// <summary>
        /// Saves the stream received from the network to a resource on the file system,  must receive a ResultType.Success or the client must redo
        /// the transfer.  If not complied with, data could be inconsistent.
        /// </summary>
        /// <param name="guid">A <see cref="Guid"/> representing the unique identifier of the resource.</param>
        /// <param name="netStream">A stream providing access to the data being received over the network.</param>
        /// <param name="requestingUser">The requesting user.</param>
        /// <param name="releaseLock"><c>True</c> to release the lock on the asset after saving.</param>
        /// <param name="currentMa">A reference to an updated <see cref="Common.Data.MetaAsset"/>.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>Success, IOError, SerializationError, InstantiationError or ResourceIsLocked.</returns>
        public ResultType SaveData(Guid guid, System.IO.Stream netStream, string requestingUser, bool releaseLock,
            out MetaAsset currentMa, out string errorMessage)
        {
            DataAsset da;
            ResultType result;
            Version currentVersion;
            Common.FileSystem.IOStream iostream;

            if ((result = LoadMeta(guid, true, true, requestingUser, false, out currentMa, out errorMessage)) != ResultType.Success)
                return result;

            da = new DataAsset(currentMa, _fileSystem);

            // Get Version
            currentVersion = new Version(new FullAsset(currentMa, da));

            if (currentMa.DataVersion >= 1)
            {
                // Exists already

                // Copy using the version scheme - copies the data asset
                if (!currentVersion.CopyDataUsingVersionScheme())
                {
                    errorMessage = "Failed to copy the current data asset into version history.";
                    return ResultType.IOError;
                }
            }

            // Get the stream to the data file
            iostream = da.GetWriteStream();

            iostream.CopyFrom(netStream);

            _fileSystem.Close(iostream);
            netStream.Close();
            netStream.Dispose();
            
            // Integrity checking
            if(da.Resource.FileLength != currentMa.Length)
            {
                errorMessage = "The file is supposed to be " + currentMa.Length.ToString() + 
                    "; however, only " + currentMa.Resource.FileLength.ToString() + 
                    " bytes of data were received.  This file is corrupt and will be deleted from the server.";
                da.Resource.DeleteFromFilesystem();
                return ResultType.LengthMismatch;
            }
            else if(!da.Resource.VerifyMd5(currentMa.Md5))
            {
                errorMessage = "The file has failed integrity checking, this means that the data received is " +
                    "not the same as the data expected.  The file is corrupt and will be deleted from the server";
                da.Resource.DeleteFromFilesystem();
                return ResultType.Md5Mismatch;                
            }

            // If we make it to here, the file is received, it has passed integrity checking

            // Update the lock
            if (releaseLock)
                currentMa.ReleaseLock();
            else
                currentMa.ApplyLock(DateTime.Now, requestingUser);
            
            // Increment the data version
            currentVersion.IncrementDataVersion();

            // Update the meta asset to disk - it was updated earlier if needed
            currentMa.Save();

            return ResultType.Success;
        }

        ///// <summary>
        ///// Deletes all versions of a specified resouce (this cannot be undone)
        ///// </summary>
        ///// <param name="guid"></param>
        ///// <param name="requestingUser"></param>
        ///// <param name="errorMessage"></param>
        ///// <returns></returns>
        //public ResultType DeleteAllVersions(Guid guid, string requestingUser, out string errorMessage)
        //{
        //    MetaAsset ma;
        //    ResultType result;
        //    List<string> allFiles = new List<string>();
        //    List<Exception> exceptions;
        //    string[] tempFiles;
        //    AssetType metaAssetType = new AssetType(AssetType.Meta);
        //    string metapath = new Common.Data.AssetType(Common.Data.AssetType.Meta).VirtualPath +"\\";
        //    string datapath = new Common.Data.AssetType(Common.Data.AssetType.Data).VirtualPath + "\\";

        //    if ((result = LoadMeta(guid, true, true, requestingUser, false, out ma, out errorMessage)) != ResultType.Success)
        //        return result;

        //    // Matches only files starting with the specified guid with a version # and ending in .xml in the specified directory (non-recursive)
        //    tempFiles = System.IO.Directory.GetFiles(metapath, guid.ToString("N") + "_*" + metaAssetType.ToString(), System.IO.SearchOption.TopDirectoryOnly);
        //    for (int i = 0; i < tempFiles.Length; i++)
        //        allFiles.Add(tempFiles[i]);
        //    // Current metaasset file
        //    allFiles.Add(metapath + guid.ToString("N") + metaAssetType.ToString());

        //    // Matches only files starting with the specified guid with a version # and ending with any extension in the specified directory (non-recursive)
        //    tempFiles = System.IO.Directory.GetFiles(datapath, guid.ToString("N") + "_*", System.IO.SearchOption.TopDirectoryOnly);
        //    for (int i = 0; i < tempFiles.Length; i++)
        //        allFiles.Add(tempFiles[i]);
        //    // Current metaasset file
        //    allFiles.Add(datapath + guid.ToString("N") + ma.Extension);

        //    // Anytime a user is working on any version of a resource, the most recent version must be locked.
        //    // Thus, since we earlier checked locks, we know that we have an exclusive lock on this resource.
            
        //    // Filesystems are notorious for race conditions, here is one
        //    // We cannot rail off our data storage, so we cannot be 100% sure that we will be able to delete, but we can verify it within our software
            
        //    try
        //    {
        //        if (!_fileSystem.DeleteMultipleFiles(allFiles, out exceptions))
        //        {
        //            errorMessage = "The requested delete could not be performed as one or more of the files were in use by other processes in the system, please retry your operation later.";
        //            return ResultType.IOError;
        //        }
        //    }
        //    catch (System.IO.FileNotFoundException e)
        //    {
        //        errorMessage = e.Message;
        //        return ResultType.IOError;
        //    }
        //    catch (Exception e)
        //    {
        //        errorMessage = e.Message;
        //        return ResultType.IOError;
        //    }

        //    return ResultType.Success;
        //}

        /// <summary>
        /// Gets the search form from the file system.
        /// </summary>
        /// <param name="requestingUser">The requesting user.</param>
        /// <param name="searchForm">The instantiated instance of the <see cref="Common.NetworkPackage.SearchForm"/>.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>Success or Failure.</returns>
        public ResultType GetSearchForm(string requestingUser, out Common.NetworkPackage.SearchForm searchForm, out string errorMessage)
        {
            string relativeFilepath = "settings\\searchform.xml";

            errorMessage = null;
            searchForm = new Common.NetworkPackage.SearchForm();

            // TODO : setup user checking

            if (!searchForm.ReadFromFile(relativeFilepath, _fileSystem))
            {
                errorMessage = "Unable to read from the saved search form.";
                return ResultType.Failure;
            }

            return ResultType.Success;
        }

        /// <summary>
        /// Gets the meta form from the file system.
        /// </summary>
        /// <param name="requestingUser">The requesting user.</param>
        /// <param name="metaForm">The instantiated instance of the <see cref="Common.NetworkPackage.MetaForm"/>.</param>
        /// <param name="errorMessage">A string representing the cause of the error.</param>
        /// <returns>Success or Failure.</returns>
        public ResultType GetMetaForm(string requestingUser, out Common.NetworkPackage.MetaForm metaForm, out string errorMessage)
        {
            string relativeFilepath = "settings\\metapropertiesform.xml";

            errorMessage = null;
            metaForm = new Common.NetworkPackage.MetaForm();

            // TODO : setup user checking

            try
            {
                if (!metaForm.ReadFromFile(relativeFilepath, _fileSystem))
                {
                    errorMessage = "Unable to read from the saved search form.";
                    return ResultType.Failure;
                }
            }
            catch (System.IO.FileNotFoundException)
            {
                errorMessage = "Unable to read from the saved search form.";
                return ResultType.Failure;
            }

            return ResultType.Success;
        }
    }
}
