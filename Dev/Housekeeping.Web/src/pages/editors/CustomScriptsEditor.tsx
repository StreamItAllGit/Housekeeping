// @module: Housekeeping.Web/pages/editors/CustomScriptsEditor
// @purpose: Module 7 (Custom Scripts & CLI Tasks) editor with 2-line hover hints and localhost raw-shell guard.
// @depends-on: react, ../../components/HybridPathPicker, ../../context/AppStateContext, ../../hints/customScripts, ../../types/models
// @exports: CustomScriptsEditor
import React from 'react';
import { HybridPathPicker } from '../../components/HybridPathPicker';
import { useAppState } from '../../context/AppStateContext';
import { CUSTOM_SCRIPTS_HINTS } from '../../hints/customScripts';
import type { CustomScriptsConfig } from '../../types/models';

interface Props {
  cfg: CustomScriptsConfig;
  onChange: (mutator: (d: CustomScriptsConfig) => void) => void;
}

export const CustomScriptsEditor: React.FC<Props> = ({ cfg, onChange }) => {
  const { authStatus } = useAppState();
  const rawBlocked = Boolean(authStatus?.rawShellBlockedForCaller);

  return (
    <>
      <div className="hk-info-banner">
        <strong>⚡ What This Module Does: Custom Scripts &amp; CLI Tasks</strong>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
          Executes custom <code>.bat</code>, <code>.cmd</code>, <code>.ps1</code> PowerShell,
          Python scripts, or CLI utilities with live output capture, exit-code auditing, and
          optional network path availability checks.
        </div>
      </div>

      {rawBlocked && (
        <div className="hk-alert-banner">
          <span>
            🔒 <strong>Raw Shell Restricted to Localhost:</strong> Viewing Custom Tasks in read-only
            mode for remote sessions.
          </span>
        </div>
      )}

      <div className="hk-card">
        <div className="hk-card-header">
          <div>
            <h3 className="hk-card-title">📜 Ordered Script &amp; Command Pipeline</h3>
            <p className="hk-card-subtitle">
              Tasks run sequentially from top to bottom when this job executes.
            </p>
          </div>
          <button
            type="button"
            className="hk-btn sm primary"
            disabled={rawBlocked}
            onClick={() =>
              onChange((d) => {
                d.Tasks.push({
                  Enabled: true,
                  Name: `Task ${d.Tasks.length + 1}`,
                  CommandOrScriptPath: '',
                  Arguments: '',
                  WorkingDirectory: '',
                  RequiredPathCheck: '',
                  RunInExternalCmd: false,
                });
              })
            }
          >
            + Add Script Task
          </button>
        </div>

        <div className="table-wrapper" tabIndex={0} role="region" aria-label="Custom script tasks">
          <table className="hk-table stack-mobile">
            <thead>
              <tr>
                <th className="hk-has-hint" style={{ width: 48 }} title={CUSTOM_SCRIPTS_HINTS.thOn}>On</th>
                <th className="hk-has-hint" style={{ width: 145 }} title={CUSTOM_SCRIPTS_HINTS.thTaskName}>Task Name</th>
                <th className="hk-has-hint" title={CUSTOM_SCRIPTS_HINTS.thScript}>Executable or Script (.bat / .ps1 / .py / .exe)</th>
                <th className="hk-has-hint" title={CUSTOM_SCRIPTS_HINTS.thArgs}>Arguments</th>
                <th className="hk-has-hint" title={CUSTOM_SCRIPTS_HINTS.thWorkDir}>Working Dir</th>
                <th className="hk-has-hint" title={CUSTOM_SCRIPTS_HINTS.thReqPath}>Required Path Check</th>
                <th className="hk-has-hint" style={{ width: 65 }} title={CUSTOM_SCRIPTS_HINTS.thExtCmd}>Ext CMD</th>
                <th className="hk-has-hint" style={{ width: 60 }} title={CUSTOM_SCRIPTS_HINTS.thDelete}>Del</th>
              </tr>
            </thead>
            <tbody>
              {(cfg.Tasks || []).length === 0 && (
                <tr>
                  <td colSpan={8} style={{ color: 'var(--text-secondary)' }}>
                    No custom tasks added yet.
                  </td>
                </tr>
              )}
              {(cfg.Tasks || []).map((t, idx) => (
                <tr key={idx}>
                  <td data-label="Enabled">
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={CUSTOM_SCRIPTS_HINTS.taskEnabled}
                      disabled={rawBlocked}
                      checked={t.Enabled}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Tasks[idx].Enabled = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Task Name">
                    <input
                      type="text"
                      className="hk-input"
                      disabled={rawBlocked}
                      value={t.Name}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Tasks[idx].Name = e.target.value;
                        })
                      }
                    />
                  </td>
                  <td data-label="Script / Command">
                    <HybridPathPicker
                      value={t.CommandOrScriptPath}
                      mode="file"
                      compactButton
                      onChange={(next) => {
                        if (!rawBlocked) {
                          onChange((d) => {
                            d.Tasks[idx].CommandOrScriptPath = next;
                          });
                        }
                      }}
                    />
                  </td>
                  <td data-label="Arguments">
                    <input
                      type="text"
                      className="hk-input mono"
                      disabled={rawBlocked}
                      value={t.Arguments}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Tasks[idx].Arguments = e.target.value;
                        })
                      }
                      placeholder="--flag value"
                    />
                  </td>
                  <td data-label="Working Dir">
                    <HybridPathPicker
                      value={t.WorkingDirectory}
                      compactButton
                      onChange={(next) => {
                        if (!rawBlocked) {
                          onChange((d) => {
                            d.Tasks[idx].WorkingDirectory = next;
                          });
                        }
                      }}
                    />
                  </td>
                  <td data-label="Required Path">
                    <input
                      type="text"
                      className="hk-input mono"
                      disabled={rawBlocked}
                      value={t.RequiredPathCheck}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Tasks[idx].RequiredPathCheck = e.target.value;
                        })
                      }
                      placeholder="Optional V:\ check"
                    />
                  </td>
                  <td data-label="External CMD">
                    <input
                      type="checkbox"
                      className="hk-has-hint"
                      title={CUSTOM_SCRIPTS_HINTS.externalCmd}
                      disabled={rawBlocked}
                      checked={t.RunInExternalCmd}
                      onChange={(e) =>
                        onChange((d) => {
                          d.Tasks[idx].RunInExternalCmd = e.target.checked;
                        })
                      }
                    />
                  </td>
                  <td data-label="Delete">
                    <button
                      type="button"
                      className="hk-btn sm danger"
                      disabled={rawBlocked}
                      onClick={() => onChange((d) => d.Tasks.splice(idx, 1))}
                    >
                      ✕
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
};
