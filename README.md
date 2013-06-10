EFModelUpgradeTool
==================

A useful tool for upgrading EF3 models to use constrained associations. I've used this on a number of client projects
to allow me the ability to set foreign key entities by ID rather than having to load them from the database.

Usage
-----

You need to perform the following steps:

1. Upgrade your project using Visual Studio 2012 to .Net 4 or later
2. Save the EDMX files you should see that Visual Studio has changed the namespaces on a number of elements
3. Compile the EdmxForeignKeyMapper solution
4. From the command line run the EdmxForeignKeyMapper.exe as follows
 
    EdmxForeignKeyMapper.exe <path to edmx file>

where <path to edmx file> is the location of your model to upgrade.

Testing
-------

Having performed the upgrade open your model in Visual Studio, right click on the model and choose validate model.
Hopefully you will see no errors.

Notes
-----

This tool makes a number of assumptions about the EDMX file. You may find that you have to make changes to the tool if you encounter issues.

If you fix a problem please send me a pull request :)



