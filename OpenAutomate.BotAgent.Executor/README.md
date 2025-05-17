# OpenAutomate Bot Agent Executor

## Overview

The OpenAutomate Bot Agent Executor is a console application that handles the execution of Python automation scripts. It's designed to be launched by the OpenAutomate Bot Agent Service to run tasks in isolated environments with proper resource management.

## Key Features

- **Task Isolation**: Each execution runs in its own process, providing isolation from the service
- **Lock Files**: Ensures only one instance of a task is running at any time
- **Virtual Environments**: Creates and manages Python virtual environments for each execution
- **Package Management**: Installs required Python packages for each task
- **Logging**: Detailed logging of execution status and output
- **Error Handling**: Graceful handling of execution errors with proper cleanup

## Execution Flow

1. **Initialization**: The executor starts with an execution ID parameter
2. **Lock Creation**: Creates a lock file to prevent duplicate task execution
3. **Task Info Retrieval**: Reads execution details from the task directory
4. **Virtual Environment Setup**: Creates a Python virtual environment for the task
5. **Requirements Installation**: Installs any required Python packages
6. **Script Execution**: Runs the Python script with provided arguments
7. **Cleanup**: Removes temporary files and releases the lock

## Directory Structure

The executor uses the following directory structure:

```
%ProgramData%\OpenAutomate\BotAgent\
├── Tasks\
│   └── {execution-id}\
│       ├── execution.json    # Task configuration
│       ├── script.py         # Python script to execute
│       ├── output.log        # Script standard output
│       └── error.log         # Script standard error
├── Locks\
│   └── {execution-id}.lock   # Lock file for running tasks
├── Temp\
│   └── {execution-id}\       # Temporary files for the execution
└── venvs\
    └── {execution-id}\       # Python virtual environment
```

## Task Lock Mechanism

The lock file system prevents duplicate execution of the same task:

1. When a task starts, it attempts to create a lock file with the execution ID
2. If the lock file already exists, the task will not start
3. The lock file contains details such as start time, machine name, and process ID
4. On task completion or failure, the lock file is removed
5. The service can detect orphaned locks (from crashed tasks) based on age or process status

## Virtual Environment

For each task, a dedicated Python virtual environment is created:

1. A new virtual environment is created for each execution
2. Required packages are installed in isolation from the system Python
3. This prevents package version conflicts between different automations
4. The virtual environment is removed after task completion if configured

## Integration with Bot Agent Service

The Bot Agent Service will use the executor in the following way:

1. The service receives a task execution command from the OpenAutomate server
2. It creates a task directory with execution configuration and script
3. It launches the executor as a separate process with the execution ID
4. The service can monitor the executor's process status
5. After completion, the service will report results back to the OpenAutomate server

## Example Execution JSON

```json
{
  "ExecutionId": "12345678-1234-1234-1234-123456789012",
  "ScriptPath": "script.py",
  "Arguments": "--input-file data.csv --output-file results.csv",
  "Requirements": [
    "pandas==1.5.3",
    "requests>=2.28.2",
    "openpyxl"
  ]
}
```

## Future Enhancements

- Add support for Python package management via pip or Pipenv
- Implement timeout handling for long-running tasks
- Add support for task cancellation
- Create a mechanism for progress reporting back to the service
- Implement resource usage monitoring and limits

## Error Handling

The executor handles errors at different stages:

1. **Invalid Arguments**: Returns error code 1
2. **Lock Creation Failure**: Returns error code 2
3. **Missing Execution Info**: Returns error code 3
4. **Invalid Execution Info**: Returns error code 4
5. **Virtual Environment Failure**: Returns error code 5
6. **Requirements Installation Failure**: Returns error code 6
7. **Script Execution Error**: Returns the script's exit code
8. **Unexpected Errors**: Returns error code 99

## Implementation Notes

- The executor is implemented in C# and runs Python scripts via Process.Start
- Dependencies are minimal to ensure reliable execution
- Lock files use simple file-based locking for maximum compatibility
- Logging uses Serilog for structured logging with file and console output 