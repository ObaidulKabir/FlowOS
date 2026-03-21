using System;
using System.IO;
using Xunit;
using FlowOS.Infrastructure.Services;

namespace FlowOS.UnitTests.Infrastructure
{
    public class WorkingDirectoryTests
    {
        [Fact]
        public void ValidatePath_ShouldPass_WhenPathIsBinaryDirectory()
        {
            // Arrange
            // Simulate a path that ends with bin/Debug/net8.0
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyProject");
            var binPath = Path.Combine(tempRoot, "bin", "Debug", "net8.0");
            
            Directory.CreateDirectory(binPath); // Path must exist to trigger InvalidOperation
            
            try
            {
                // Act & Assert
                // Relaxed validation: Should NOT throw
                WorkingDirectoryValidator.Validate(binPath);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void ValidatePath_ShouldThrow_WhenPathDoesNotExist()
        {
            // Arrange
            var nonExistentPath = @"C:\NonExistent\Project";
            
            // Act & Assert
            var ex = Assert.Throws<DirectoryNotFoundException>(() => 
                WorkingDirectoryValidator.Validate(nonExistentPath));
        }

        [Fact]
        public void ValidatePath_ShouldPass_WhenPathIsValidProjectRoot()
        {
            // Arrange
            var tempPath = Path.GetTempPath();
            var projectPath = Path.Combine(tempPath, "ValidProject");
            Directory.CreateDirectory(projectPath);
            
            try
            {
                // Act
                WorkingDirectoryValidator.Validate(projectPath);
                // Assert - No exception
            }
            finally
            {
                Directory.Delete(projectPath);
            }
        }
    }
}
