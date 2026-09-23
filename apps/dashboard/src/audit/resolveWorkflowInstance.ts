import { WorkflowInstance } from '../types';

export const resolveWorkflowInstanceId = (
  instances: WorkflowInstance[],
  idOrCorrelation?: string | null
): string | null => {
  if (!idOrCorrelation) return null;
  const match = instances.find((item) =>
    item.id === idOrCorrelation ||
    item.workflowId === idOrCorrelation ||
    item.correlationId === idOrCorrelation
  );
  return match?.id || match?.workflowId || idOrCorrelation;
};
