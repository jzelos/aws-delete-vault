# aws-delete-vault

This is a C# Console App using .Net and the AWS Glacier SDK. 

The idea is to delete large vaults with too many archives to parse the inventory in memory.

* The app will browse existing jobs to see if one has been requested and request one if not.
* It will wait until the inventory job is complete, then download inventory (as CSV) and save locally.
* It will then read as a stream and delete all archives
* And then attempt to remove the vault. 

The code works, but is very rough and uncommented with no error handling, it was written for one use only.
