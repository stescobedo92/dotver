using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Shell completion scripts. Dynamic values come from `dotver __complete`,
/// whose output is restricted to shell-safe tokens (see CompletionDataService).
/// No script uses eval.
/// </summary>
public static class CompletionScripts
{
    private const string Commands =
        "dashboard home list ls current use local pin default alias unalias ensure sync install scan " +
        "audit outdated upgrade list-remote ls-remote uninstall doctor prune register-root exec each which shell env completions deactivate";

    public static string Render(ShellKind shell) =>
        shell switch
        {
            ShellKind.PowerShell => PowerShell,
            ShellKind.Bash => Bash,
            ShellKind.Zsh => Zsh,
            ShellKind.Fish => Fish,
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Supported shells: powershell, bash, zsh, fish.")
        };

    private static string PowerShell => $$"""
        # dotver completion (PowerShell). Append this script to your profile:
        #   dotver completions powershell >> $PROFILE
        Register-ArgumentCompleter -CommandName dotver -Native -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $elements = @($commandAst.CommandElements | ForEach-Object { $_.Extent.Text })
            $command = if ($elements.Count -gt 1) { $elements[1] } else { '' }
            $candidates = @()
            if ($elements.Count -le 2 -and $wordToComplete -notlike '-*') {
                $candidates = '{{Commands}}'.Split(' ')
            }
            elseif ($command -in 'use','default') {
                $candidates = @(dotver __complete tokens 2>$null)
            }
            elseif ($command -in 'local','pin','exec','each','which','install','shell','env') {
                $candidates = @(dotver __complete sdk-tokens 2>$null)
            }
            elseif ($command -in 'uninstall','upgrade') {
                $candidates = @(dotver __complete versions 2>$null)
            }
            elseif ($command -eq 'unalias') {
                $candidates = @(dotver __complete aliases 2>$null)
            }
            elseif ($command -eq 'completions') {
                $candidates = @('powershell','bash','zsh','fish')
            }
            $candidates |
                Where-Object { $_ -like "$wordToComplete*" } |
                ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }
        }

        """;

    private static string Bash => $$"""
        # dotver completion (bash). Add to ~/.bashrc:
        #   source <(dotver completions bash)
        # Dynamic candidates are read line-by-line and never re-expanded
        # (no compgen -W on command output, which would execute $() tokens).
        _dotver_from() {
            local kind="$1" line
            while IFS= read -r line; do
                [[ $line == "$cur"* ]] && COMPREPLY+=("$line")
            done < <(dotver __complete "$kind" 2>/dev/null)
        }
        _dotver() {
            local cur cmd
            COMPREPLY=()
            cur="${COMP_WORDS[COMP_CWORD]}"
            cmd="${COMP_WORDS[1]:-}"
            if [[ $COMP_CWORD -eq 1 ]]; then
                local word
                for word in {{Commands}}; do
                    [[ $word == "$cur"* ]] && COMPREPLY+=("$word")
                done
                return 0
            fi
            case "$cmd" in
                use|default) _dotver_from tokens ;;
                local|pin|exec|each|which|install|shell|env) _dotver_from sdk-tokens ;;
                uninstall|upgrade) _dotver_from versions ;;
                unalias) _dotver_from aliases ;;
                completions)
                    local shell
                    for shell in powershell bash zsh fish; do
                        [[ $shell == "$cur"* ]] && COMPREPLY+=("$shell")
                    done ;;
            esac
        }
        complete -F _dotver dotver

        """;

    private static string Zsh => $$"""
        #compdef dotver
        # dotver completion (zsh). Save as _dotver in a directory on your fpath, or:
        #   source <(dotver completions zsh)
        _dotver() {
            local -a candidates
            if (( CURRENT == 2 )); then
                candidates=({{Commands}})
            else
                case "${words[2]}" in
                    use|default)
                        candidates=(${(f)"$(dotver __complete tokens 2>/dev/null)"}) ;;
                    local|pin|exec|each|which|install|shell|env)
                        candidates=(${(f)"$(dotver __complete sdk-tokens 2>/dev/null)"}) ;;
                    uninstall|upgrade)
                        candidates=(${(f)"$(dotver __complete versions 2>/dev/null)"}) ;;
                    unalias)
                        candidates=(${(f)"$(dotver __complete aliases 2>/dev/null)"}) ;;
                    completions)
                        candidates=(powershell bash zsh fish) ;;
                esac
            fi
            compadd -- $candidates
        }
        compdef _dotver dotver

        """;

    private const string Fish = """
        # dotver completion (fish). Save to ~/.config/fish/completions/dotver.fish
        complete -c dotver -f
        complete -c dotver -n '__fish_use_subcommand' -a 'dashboard home list ls current use local pin default alias unalias ensure sync install scan audit outdated upgrade list-remote ls-remote uninstall doctor prune register-root exec each which shell env completions deactivate'
        complete -c dotver -n '__fish_seen_subcommand_from use default' -a '(dotver __complete tokens 2>/dev/null)'
        complete -c dotver -n '__fish_seen_subcommand_from local pin exec each which install shell env' -a '(dotver __complete sdk-tokens 2>/dev/null)'
        complete -c dotver -n '__fish_seen_subcommand_from uninstall upgrade' -a '(dotver __complete versions 2>/dev/null)'
        complete -c dotver -n '__fish_seen_subcommand_from unalias' -a '(dotver __complete aliases 2>/dev/null)'
        complete -c dotver -n '__fish_seen_subcommand_from completions' -a 'powershell bash zsh fish'

        """;
}



