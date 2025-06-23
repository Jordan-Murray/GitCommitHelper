# Local AI Git Helper

An interactive C# console application that uses a locally running LLM to generate descriptive Pull Request titles and descriptions from your commit history.

This tool is a developer's local AI sidekick, providing intelligent suggestions directly in your terminal without sending your code to a third-party API.

## Features

* **Interactive UI:** A clean, modern terminal interface powered by `Spectre.Console`.
* **Local & Private:** Code analysis happens entirely on your machine.
* **Repository Discovery:** Automatically finds all your local Git projects.
* **AI-Powered Generation:** Select any commit on a branch to generate PR details based on that commit's specific changes.

## Prerequisites

* .NET SDK (8.0 or later)
* Git (accessible from your system's PATH)
* Docker Desktop with the **Model Runner** Beta feature enabled, including **host-side TCP support**.
* A local GGUF-compatible model pulled and running in Docker (e.g., `ai/gemma3:latest`, `mistralai/mistral`, etc.).

## Getting Started

1. **Clone the Repository:**
   ```bash
   git clone <your-repo-url> && cd <your-repo-folder>
   ```

2. **Configure `appsettings.json`:**
   Update the `RepositorySearchRoot` to point to your projects folder and ensure the `Model` name matches the one running in Docker.
   ```json
   {
     "Settings": {
       "RepositorySearchRoot": "C:\\Path\\To\\Your\\Projects",
       "BaseBranchForPRs": "main"
     },
     "OpenAI": {
       "Model": "ai/gemma3:latest"
     }
   }
   ```

3. **Run the App:**
   Make sure your local model is running in Docker Desktop, then execute:
   ```bash
   dotnet run
   ```

Navigate the menus using the arrow keys and `Enter`.
