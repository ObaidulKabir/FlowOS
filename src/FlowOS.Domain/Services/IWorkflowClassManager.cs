﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;

namespace FlowOS.Domain.Services;

public interface IWorkflowClassManager 
{ 
    ValidationResult ApproveAsPublic(WorkflowClass workflowClass); 
    ValidationResult CreateDraft(WorkflowClass workflowClass); 
    ValidationResult Deprecate(WorkflowClass workflowClass); 
    ValidationResult Publish(WorkflowClass workflowClass); 
    ValidationResult SubmitForReview(WorkflowClass workflowClass); 
    ValidationResult ValidateOnly(WorkflowClass workflowClass); 
    ValidationResult WithdrawSubmission(WorkflowClass workflowClass); 
}